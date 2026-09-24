using System.Security.Cryptography;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Groups;

/// <summary>
/// Cohort forms (waivers and questions) plus the canonical WSL group: 14 attendees at $450,
/// 9 Complete and 5 Incomplete, one withdrawal request waiting for the leader. The leader is the
/// Pastor Dave persona, since the WorkOS emulator has no Carmen Ortiz user (see the ExecPlan).
/// </summary>
public sealed class GroupsSeed : ISeedModule
{
    public const string LeaderEmail = "pastor.dave@example.com";
    public const string GroupName = "Northside Fellowship leaders";

    /// <inheritdoc />
    public int Order => 100;

    static readonly (string Name, string? Email, bool Complete)[] Roster =
    [
        ("Alex Chen", "alex.chen@example.com", true),
        ("Brianna Scott", "brianna.scott@example.com", true),
        ("Caleb Turner", "caleb.turner@example.com", true),
        ("Danielle Brooks", "danielle.brooks@example.com", true),
        ("Ethan Parker", "ethan.parker@example.com", true),
        ("Faith Nguyen", "faith.nguyen@example.com", true),
        ("Gabriel Reed", "gabriel.reed@example.com", true),
        ("Hannah Mitchell", "hannah.mitchell@example.com", true),
        ("Isaiah Wright", "isaiah.wright@example.com", true),
        ("Jasmine Lee", "jasmine.lee@example.com", false),
        ("Kevin Morales", "kevin.morales@example.com", false),
        ("Lily Carter", "lily.carter@example.com", false),
        ("Mateo Silva", "mateo.silva@example.com", false),
        ("Noah Bennett", null, false),
    ];

    /// <inheritdoc />
    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        var cohort = await db.Programs.Include(p => p.Waivers).Include(p => p.Questions).Include(p => p.Sessions).ThenInclude(s => s.Pools)
            .FirstOrDefaultAsync(p => p.Slug == "emerging-leaders-cohort", ct);
        if (cohort is null) return;

        if (cohort.Waivers.Count == 0)
        {
            cohort.Waivers.AddRange(
                new WaiverTemplate { Title = "Participant Release and Waiver of Liability", Version = 3, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = ReleaseText },
                new WaiverTemplate { Title = "Photo and Media Release", Version = 2, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = PhotoText });
        }
        if (cohort.Questions.Count == 0)
        {
            cohort.Questions.AddRange(
                new Question { Key = "emergencyName", Label = "Emergency contact name", Type = QuestionType.Text, Scope = QuestionScope.Participant, Required = true, SortOrder = 1 },
                new Question { Key = "emergencyPhone", Label = "Emergency contact phone", Type = QuestionType.Text, Scope = QuestionScope.Participant, Required = true, SortOrder = 2 },
                new Question { Key = "tshirt", Label = "T-shirt size", Type = QuestionType.Select, Scope = QuestionScope.Participant, Required = true, Options = "Adult S|Adult M|Adult L|Adult XL|Adult 2XL", SortOrder = 3 },
                new Question { Key = "dietary", Label = "Dietary needs or allergies", Type = QuestionType.Text, Scope = QuestionScope.Participant, Required = false, SortOrder = 4 });
        }
        await db.SaveChangesAsync(ct);

        if (await db.Set<GroupRegistration>().AnyAsync(ct)) return;
        var session = cohort.Sessions.OrderBy(s => s.StartDate).First();

        var household = await db.Households.FirstOrDefaultAsync(h => h.Email == LeaderEmail, ct);
        if (household is null)
        {
            household = new Household { Name = "Kim", Email = LeaderEmail, Phone = "(706) 555-0188", City = "Rome, GA" };
            household.Members.Add(new Person { FirstName = "Dave", LastName = "Kim", DateOfBirth = new(1979, 4, 14), Gender = Gender.Male, IsAdult = true, Role = "Primary", Email = LeaderEmail });
            db.Households.Add(household);
            await db.SaveChangesAsync(ct);
        }

        var paidAt = new DateTime(2028, 2, 2, 15, 12, 0, DateTimeKind.Utc);
        var total = session.PriceCents * Roster.Length;
        var order = new PaymentOrder
        {
            HouseholdId = household.Id,
            SessionId = session.Id,
            IdempotencyKey = $"seed-group-{Guid.NewGuid():N}",
            ConfirmationCode = "WS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)),
            PaymentOption = PaymentOption.Full,
            SubtotalCents = total,
            TotalCents = total,
            DueTodayCents = total,
            Status = OrderStatus.Paid,
            CreatedAt = paidAt,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = total, Succeeded = true, ProcessorRef = "fsv_seedgroup01", CardLast4 = "4242", CreatedAt = paidAt });
        db.Orders.Add(order);

        var group = new GroupRegistration
        {
            SessionId = session.Id,
            LeaderHouseholdId = household.Id,
            LeaderName = "Dave Kim",
            Name = GroupName,
            Status = GroupStatus.Confirmed,
            Order = order,
            CreatedAt = paidAt.AddMinutes(-20),
            ConfirmedAt = paidAt,
        };
        for (var i = 0; i < Roster.Length; i++)
        {
            var (name, email, complete) = Roster[i];
            var a = new GroupAttendee
            {
                Name = name,
                Email = email,
                SortOrder = i,
                // Random tokens nobody knows: "Resend link" issues a fresh one for the demo.
                TokenHash = email is null ? null : GroupService.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                LinkSentAt = email is null ? null : paidAt,
                FormStatus = complete ? FormStatus.Complete : FormStatus.Incomplete,
            };
            if (complete)
            {
                var submitted = paidAt.AddDays(1 + i);
                a.SubmittedAt = submitted;
                a.AnswersJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["emergencyName"] = "Parent or spouse",
                    ["emergencyPhone"] = "(706) 555-0100",
                    ["tshirt"] = "Adult M",
                });
                foreach (var w in cohort.Waivers)
                    a.WaiverAcceptances.Add(new GroupWaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = name, AcceptedAt = submitted });
            }
            if (name == "Mateo Silva")
            {
                a.Withdrawal = WithdrawalStatus.Requested;
                a.WithdrawalReason = "I start a new job that week and can't get the days off.";
                a.WithdrawalRequestedAt = paidAt.AddDays(12);
            }
            group.Attendees.Add(a);
        }
        db.Add(group);

        // The core seed already reserved 14 cohort seats for this group; only top up if it didn't.
        var pool = session.Pools.OrderBy(p => p.SortOrder).First();
        if (pool.Reserved < Roster.Length) pool.Reserved = Roster.Length;
        await db.SaveChangesAsync(ct);
    }

    const string ReleaseText = """
        In consideration of my participation in the WinShape Leadership Emerging Leaders Cohort, I acknowledge that retreat activities, including outdoor team exercises, recreation, and travel between buildings on the Rome, GA campus, carry inherent risks of injury.

        I understand that WinShape Foundation, its staff, and volunteers take reasonable precautions but cannot eliminate all risk. I release WinShape Foundation from claims arising from ordinary negligence related to my participation, to the extent permitted by Georgia law.

        I authorize WinShape staff to obtain emergency medical treatment for me if I cannot consent, and I accept responsibility for the cost of that care.
        """;

    const string PhotoText = """
        WinShape may photograph or record cohort sessions. I grant WinShape Foundation permission to use these images in printed materials, on its websites, and on its social media accounts, without compensation.

        Images will not be captioned with my full name. I may withdraw this permission at any time by contacting WinShape; withdrawal applies to future use.
        """;
}
