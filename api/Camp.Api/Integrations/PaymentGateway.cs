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

    public int ChargeCount { get; private set; }

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
            if (digits.EndsWith("0002"))
                return new GatewayResult(false, $"fsv_{Guid.NewGuid():N}"[..16], last4, "Your card was declined. Try another card or contact your bank.");
            ChargeCount++;
            return new GatewayResult(true, $"fsv_{Guid.NewGuid():N}"[..16], last4, null);
        });
    }

    public Task<GatewayResult?> LookupAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_byKey.TryGetValue(idempotencyKey, out var r) ? r : null);

    public Task<GatewayResult> RefundAsync(string processorRef, int amountCents, CancellationToken ct = default) =>
        Task.FromResult(new GatewayResult(true, $"fsv_rf_{Guid.NewGuid():N}"[..16], "", null));
}
