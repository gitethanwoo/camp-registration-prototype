using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Data;

public class CampDbContext(DbContextOptions<CampDbContext> options) : DbContext(options)
{
    public DbSet<Ministry> Ministries => Set<Ministry>();
    public DbSet<CampProgram> Programs => Set<CampProgram>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<CapacityPool> CapacityPools => Set<CapacityPool>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<WaiverTemplate> WaiverTemplates => Set<WaiverTemplate>();
    public DbSet<PaymentOrder> Orders => Set<PaymentOrder>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<WaiverAcceptance> WaiverAcceptances => Set<WaiverAcceptance>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<PaymentOperation> PaymentOperations => Set<PaymentOperation>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CampProgram>().ToTable("Programs");
        b.Entity<CampProgram>().HasIndex(p => p.Slug).IsUnique();
        b.Entity<CampProgram>().Property(p => p.Description).HasMaxLength(4000);
        b.Entity<CampProgram>().HasMany(p => p.Questions).WithOne().HasForeignKey(q => q.ProgramId);
        b.Entity<CampProgram>().HasMany(p => p.Waivers).WithOne().HasForeignKey(w => w.ProgramId);
        b.Entity<Ministry>().HasIndex(m => m.Code).IsUnique();

        b.Entity<CapacityPool>().ToTable(t => t.HasCheckConstraint("CK_Pool_Reserved", "[Reserved] >= 0 AND [Reserved] <= [Capacity]"));

        b.Entity<WaiverTemplate>().Property(w => w.Body).HasMaxLength(8000);

        b.Entity<PaymentOrder>().ToTable("PaymentOrders");
        b.Entity<PaymentOrder>().HasIndex(o => o.IdempotencyKey).IsUnique();
        b.Entity<PaymentOrder>().HasIndex(o => o.ConfirmationCode).IsUnique();
        b.Entity<PaymentOrder>().HasOne(o => o.Session).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.Entity<PaymentOrder>().HasMany(o => o.Operations).WithOne().HasForeignKey(x => x.OrderId);
        b.Entity<PaymentOrder>().HasMany(o => o.Installments).WithOne().HasForeignKey(x => x.OrderId);

        b.Entity<Registration>().Property(r => r.AnswersJson).HasMaxLength(4000);
        b.Entity<Registration>().Property(r => r.HealthJson).HasMaxLength(4000);
        b.Entity<Registration>().HasOne(r => r.Session).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.Entity<Registration>().HasOne(r => r.Pool).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.Entity<Registration>().HasOne(r => r.Person).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.Entity<Registration>().HasIndex(r => new { r.SessionId, r.Status });
        // One active registration per person per session, enforced by the database.
        b.Entity<Registration>().HasIndex(r => new { r.SessionId, r.PersonId }).IsUnique().HasFilter("[Status] <> 'Cancelled'");
        b.Entity<Registration>().Ignore(r => r.BalanceCents);

        b.Entity<WaiverAcceptance>().HasOne(w => w.WaiverTemplate).WithMany().OnDelete(DeleteBehavior.NoAction);

        b.Entity<WaitlistEntry>().HasOne(w => w.Person).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.Entity<WaitlistEntry>().HasIndex(w => new { w.PoolId, w.Position });

        b.Entity<Person>().Ignore(p => p.FullName);
        b.Entity<DiscountCode>().HasIndex(d => d.Code).IsUnique();

        b.Entity<OutboxEvent>().Property(o => o.PayloadJson).HasMaxLength(4000);
        b.Entity<OutboxEvent>().HasIndex(o => o.ProcessedAt);
        b.Entity<AuditEvent>().Property(a => a.Detail).HasMaxLength(2000);
        b.Entity<AuditEvent>().HasIndex(a => new { a.EntityType, a.EntityId });

        // Feature slices map their own entities with IEntityTypeConfiguration<T> next to the code.
        b.ApplyConfigurationsFromAssembly(typeof(CampDbContext).Assembly);
    }
}
