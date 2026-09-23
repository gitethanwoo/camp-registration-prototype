namespace Camp.Api.Domain;

// Conceptual entities from the architecture map. Money is stored in cents.

public class Ministry
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<CampProgram> Programs { get; set; } = [];
}

public enum ProgramType { Standard, Admittance, Cohort }

// Configured per program so ON and Day Camp can use different mechanisms at the same time (FR-20).
public enum HealthMechanism { Embedded, ThirdParty, CampDoc }

public class CampProgram
{
    public int Id { get; set; }
    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgramType Type { get; set; }
    public HealthMechanism HealthMechanism { get; set; }
    public string Location { get; set; } = "";
    public string? HostOrganization { get; set; }
    public string ImageUrl { get; set; } = "";
    public bool IsPublished { get; set; }
    public List<Session> Sessions { get; set; } = [];
    public List<Question> Questions { get; set; } = [];
    public List<WaiverTemplate> Waivers { get; set; } = [];
}

public class Session
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public CampProgram Program { get; set; } = null!;
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int PriceCents { get; set; }
    public int DepositCents { get; set; }
    public int PlanInstallments { get; set; }
    /// <summary>Balance (or the final plan installment) is due on this date, before the session starts.</summary>
    public DateOnly BalanceDueDate { get; set; }
    public string WaitlistMode { get; set; } = "AdminApproval"; // FR-69: locked in v1
    public List<CapacityPool> Pools { get; set; } = [];
}

/// <summary>
/// A slice of session capacity (e.g. "Boys G6–8" or "Grade 6"). Reserved is the number of seats
/// held by registrations and outstanding waitlist offers, and is only changed with a conditional
/// UPDATE so two registrants can never both take the last seat (FR-14).
/// </summary>
public class CapacityPool
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public string Name { get; set; } = "";
    public Gender? Gender { get; set; }
    public int GradeMin { get; set; }
    public int GradeMax { get; set; }
    public int Capacity { get; set; }
    public int Reserved { get; set; }
    public int SortOrder { get; set; }
}

public enum Gender { Male, Female }

public class Household
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string City { get; set; } = "";
    public string? SalesforceId { get; set; }
    public List<Person> Members { get; set; } = [];
}

public class Person
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public bool IsAdult { get; set; }
    public string? Role { get; set; } // "Primary", "Co-owner" for adults
    public string? Email { get; set; }
    // Basic health profile (FR-21); lives on the member so returning families aren't re-asked.
    public string? Dietary { get; set; }
    public string? Allergies { get; set; }
    public string? AdaNeeds { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}

public enum QuestionType { Select, Text, YesNo }
public enum QuestionScope { Participant, Household }

public class Question
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public QuestionType Type { get; set; }
    public QuestionScope Scope { get; set; }
    public bool Required { get; set; }
    public string? Options { get; set; } // pipe-separated for Select
    public string? ShowWhenKey { get; set; }
    public string? ShowWhenValue { get; set; }
    public int SortOrder { get; set; }
}

public class WaiverTemplate
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Body { get; set; } = "";
    public bool PerParticipant { get; set; }
}

public enum PaymentOption { Deposit, Full, Plan }
public enum OrderStatus { Pending, Paid, Declined, Waitlisted }

public class PaymentOrder
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public string ConfirmationCode { get; set; } = "";
    public PaymentOption PaymentOption { get; set; }
    public string? DiscountCode { get; set; }
    public int SubtotalCents { get; set; }
    public int DiscountCents { get; set; }
    public int TotalCents { get; set; }
    public int DueTodayCents { get; set; }
    public OrderStatus Status { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Registration> Registrations { get; set; } = [];
    public List<PaymentOperation> Operations { get; set; } = [];
    public List<Installment> Installments { get; set; } = [];
}

public enum RegistrationStatus { Draft, ApplicationPending, Approved, PaymentPending, Confirmed, Waitlisted, OfferedSpot, Cancelled, Transferred }
public enum FormStatus { Complete, Incomplete, Missing, NotRequired }

public class Registration
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public PaymentOrder? Order { get; set; }
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public int PoolId { get; set; }
    public CapacityPool Pool { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int HouseholdId { get; set; }
    public RegistrationStatus Status { get; set; }
    public int Grade { get; set; }
    public int PriceCents { get; set; }
    public int DiscountCents { get; set; }
    public int PaidCents { get; set; }
    public FormStatus HealthStatus { get; set; }
    public string AnswersJson { get; set; } = "{}";
    // Embedded (non-CampDoc) health capture. Production: encrypted column + scoped access (FR-112).
    public string? HealthJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<WaiverAcceptance> WaiverAcceptances { get; set; } = [];

    public int BalanceCents => Status is RegistrationStatus.Cancelled ? 0 : PriceCents - DiscountCents - PaidCents;
}

public class WaiverAcceptance
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int WaiverTemplateId { get; set; }
    public WaiverTemplate WaiverTemplate { get; set; } = null!;
    public int Version { get; set; } // snapshot of the accepted version
    public string SignerName { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
}

public enum WaitlistStatus { Waiting, Offered, Accepted, Expired, Removed }

public class WaitlistEntry
{
    public int Id { get; set; }
    public int PoolId { get; set; }
    public CapacityPool Pool { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int HouseholdId { get; set; }
    public int? OrderId { get; set; }
    public int Position { get; set; }
    public WaitlistStatus Status { get; set; }
    public DateTime? OfferExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum PaymentKind { Charge, Authorize, Refund, Void }

public class PaymentOperation
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public PaymentKind Kind { get; set; }
    public int AmountCents { get; set; }
    public bool Succeeded { get; set; }
    public string ProcessorRef { get; set; } = "";
    public string CardLast4 { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum InstallmentStatus { Scheduled, Paid, Failed }

public class Installment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int Sequence { get; set; }
    public DateOnly DueDate { get; set; }
    public int AmountCents { get; set; }
    public InstallmentStatus Status { get; set; }
}

public enum DiscountKind { Percent, Flat }
public enum DiscountStatus { Approved, PendingApproval }

public class DiscountCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public DiscountKind Kind { get; set; }
    public int Value { get; set; } // percent, or cents per participant
    public DiscountStatus Status { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>Transactional outbox: written in the same transaction as the state change, delivered by a worker.</summary>
public class OutboxEvent
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Target { get; set; } = ""; // HubSpot, Salesforce, ...
    public string AggregateId { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>Append-only audit trail (FR-103). No update/delete endpoints exist.</summary>
public class AuditEvent
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
