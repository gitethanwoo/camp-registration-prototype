using System.Collections.Concurrent;

namespace Camp.Api.Integrations;

public record GatewayResult(bool Succeeded, string ProcessorRef, string CardLast4, string? DeclineReason);

/// <summary>
/// Port for the card processor. Production adapter: Fiserv Commerce Hub with hosted fields,
/// so card data never touches this application.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayResult> ChargeAsync(string cardToken, int amountCents, string idempotencyKey, CancellationToken ct = default);
    Task<GatewayResult?> LookupAsync(string idempotencyKey, CancellationToken ct = default);
    Task<GatewayResult> RefundAsync(string processorRef, int amountCents, CancellationToken ct = default);

    /// <summary>Holds <paramref name="amountCents"/> on the card without moving money (admittance programs, FR-46).</summary>
    Task<GatewayResult> AuthorizeAsync(string cardToken, int amountCents, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Turns an authorization into a charge. A repeated key returns the original result.</summary>
    Task<GatewayResult> CaptureAsync(string authorizationRef, int amountCents, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Releases an authorization so the hold drops off the card.</summary>
    Task<GatewayResult> VoidAsync(string authorizationRef, CancellationToken ct = default);
}

/// <summary>
/// In-memory stand-in for the Fiserv sandbox. Honors idempotency keys the way the real
/// processor does: a repeated key returns the original result instead of charging again.
/// Test cards: 4242 4242 4242 4242 approves; 4000 0000 0000 0002 declines.
/// </summary>
public class FakeFiservGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, GatewayResult> _byKey = new();
    private readonly ConcurrentDictionary<string, string> _tokens = new();

    private readonly ConcurrentDictionary<string, GatewayResult> _authByKey = new();
    private readonly ConcurrentDictionary<string, HoldState> _holds = new();
    private readonly Lock _holdLock = new();

    public int ChargeCount { get; private set; }
    public int AuthorizeCount { get; private set; }
    public int CaptureCount { get; private set; }
    public int VoidCount { get; private set; }

    enum HoldState { Open, Captured, Voided }

    /// <summary>Emulates the hosted-fields iframe: card number in, opaque token out.</summary>
    public string Tokenize(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        if (digits.Length < 12) throw new ArgumentException("Card number is incomplete.");
        var token = $"tok_{Guid.NewGuid():N}"[..20];
        _tokens[token] = digits;
        return token;
    }

    public async Task<GatewayResult> ChargeAsync(string cardToken, int amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        await Task.Delay(400, ct); // network latency, so the UI's processing state is visible
        return _byKey.GetOrAdd(idempotencyKey, _ =>
        {
            if (!_tokens.TryGetValue(cardToken, out var digits))
                return new GatewayResult(false, "", "", "Your card details expired. Please re-enter them.");
            var last4 = digits[^4..];
            if (digits.EndsWith("0002", StringComparison.Ordinal))
                return new GatewayResult(false, $"fsv_{Guid.NewGuid():N}"[..16], last4, "Your card was declined. Try another card or contact your bank.");
            ChargeCount++;
            return new GatewayResult(true, $"fsv_{Guid.NewGuid():N}"[..16], last4, null);
        });
    }

    public Task<GatewayResult?> LookupAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_byKey.TryGetValue(idempotencyKey, out var r) ? r : null);

    public Task<GatewayResult> RefundAsync(string processorRef, int amountCents, CancellationToken ct = default) =>
        Task.FromResult(new GatewayResult(true, $"fsv_rf_{Guid.NewGuid():N}"[..16], "", null));

    public async Task<GatewayResult> AuthorizeAsync(string cardToken, int amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        await Task.Delay(300, ct); // network latency, so the UI's processing state is visible
        return _authByKey.GetOrAdd(idempotencyKey, _ =>
        {
            if (!_tokens.TryGetValue(cardToken, out var digits))
                return new GatewayResult(false, "", "", "Your card details expired. Please re-enter them.");
            var last4 = digits[^4..];
            if (digits.EndsWith("0002", StringComparison.Ordinal))
                return new GatewayResult(false, "", last4, "Your card was declined. Try another card or contact your bank.");
            var reference = $"fsv_auth_{Guid.NewGuid():N}"[..20];
            _holds[reference] = HoldState.Open;
            AuthorizeCount++;
            return new GatewayResult(true, reference, last4, null);
        });
    }

    // Holds this process hasn't seen (seeded rows, or made before a restart) are trusted as open:
    // the sandbox is in-memory, and the service enforces expiry from its own timestamp.
    public Task<GatewayResult> CaptureAsync(string authorizationRef, int amountCents, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_byKey.GetOrAdd(idempotencyKey, _ =>
        {
            lock (_holdLock)
            {
                var state = _holds.GetValueOrDefault(authorizationRef, HoldState.Open);
                if (state != HoldState.Open)
                    return new GatewayResult(false, "", "", "This authorization is no longer open. Ask the family to re-enter their card.");
                _holds[authorizationRef] = HoldState.Captured;
                CaptureCount++;
                return new GatewayResult(true, $"fsv_cap_{Guid.NewGuid():N}"[..18], "", null);
            }
        }));

    public Task<GatewayResult> VoidAsync(string authorizationRef, CancellationToken ct = default)
    {
        lock (_holdLock)
        {
            var state = _holds.GetValueOrDefault(authorizationRef, HoldState.Open);
            if (state == HoldState.Captured)
                return Task.FromResult(new GatewayResult(false, authorizationRef, "", "This authorization was already captured; refund it instead."));
            if (state == HoldState.Open) VoidCount++;
            _holds[authorizationRef] = HoldState.Voided;
            return Task.FromResult(new GatewayResult(true, authorizationRef, "", null));
        }
    }
}
