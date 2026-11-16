using Microsoft.EntityFrameworkCore;
using Borrowing.Domain;

namespace Borrowing.Infrastructure;

public sealed class BorrowingDbContext(DbContextOptions<BorrowingDbContext> options) : DbContext(options)
{
    public DbSet<LoanRequest> LoanRequests => Set<LoanRequest>();
    public DbSet<LoanItem> LoanItems => Set<LoanItem>();
    public DbSet<LoanHistory> LoanHistories => Set<LoanHistory>();
    public DbSet<InventoryOperation> InventoryOperations => Set<InventoryOperation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ReminderDispatch> ReminderDispatches => Set<ReminderDispatch>();
    public string EventCorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CaptureEvents(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CaptureEvents(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void CaptureEvents()
    {
        var histories = ChangeTracker.Entries<LoanHistory>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray();
        foreach (var history in histories)
        {
            var type = history.Action switch { "SUBMITTED" => "LoanSubmitted", "APPROVED" => "LoanApproved", "REJECTED" => "LoanRejected",
                "HANDED_OVER" => "LoanHandedOver", "RETURNED" => "LoanReturned", _ => null };
            if (type == null || ChangeTracker.Entries<OutboxMessage>().Any(e => e.Entity.EventId == history.Id)) continue;
            var loan = ChangeTracker.Entries<LoanRequest>().Single(e => e.Entity.Id == history.LoanRequestId).Entity;
            OutboxMessages.Add(LoanEvents.Create(loan, type, history.Id, EventCorrelationId, history.OccurredAt));
        }
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ReminderDispatch>(b =>
        {
            b.HasKey(x => x.EventId); b.Property(x => x.EventId).ValueGeneratedNever();
            b.Property(x => x.EventType).HasMaxLength(100);
            b.HasIndex(x => new { x.LoanId, x.EventType, x.DueAt, x.ThresholdMinutes }).IsUnique();
            b.HasOne<LoanRequest>().WithMany().HasForeignKey(x => x.LoanId);
        });
        model.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.EventId); b.Property(x => x.EventId).ValueGeneratedNever();
            b.Property(x => x.EventType).HasMaxLength(100); b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.PayloadJson).HasColumnType("jsonb"); b.Property(x => x.LastError).HasMaxLength(500);
            b.HasIndex(x => new { x.NextAttemptAt, x.OccurredAt }).HasFilter("\"PublishedAt\" IS NULL");
        });
        model.Entity<InventoryOperation>(b =>
        {
            b.HasKey(x => x.OperationId);
            b.Property(x => x.OperationId).ValueGeneratedNever();
            b.Property(x => x.OperationType).HasMaxLength(32);
            b.Property(x => x.State).HasMaxLength(32);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.RequestJson).HasColumnType("jsonb");
            b.Property(x => x.LastError).HasMaxLength(500);
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.State, x.NextRetryAt });
            b.HasIndex(x => x.LoanId).IsUnique().HasFilter("\"State\" NOT IN ('COMPLETED','FAILED')");
            b.HasOne<LoanRequest>().WithMany().HasForeignKey(x => x.LoanId);
        });
        model.Entity<LoanRequest>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Purpose).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.RejectionReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.StudentId, x.CreatedAt });
            b.HasIndex(x => new { x.Status, x.CreatedAt });
            b.HasIndex(x => new { x.Status, x.DueAt });
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.LoanRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Histories).WithOne().HasForeignKey(x => x.LoanRequestId).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Loan_Due", "\"DueAt\" > \"RequestedAt\" AND \"DueAt\" <= \"RequestedAt\" + interval '30 days'");
                t.HasCheckConstraint("CK_Loan_Status", "\"Status\" IN ('PENDING','APPROVING','APPROVED','BORROWED','RETURNED','REJECTED','CANCELLED','APPROVAL_FAILED','EXPIRED')");
                t.HasCheckConstraint("CK_Loan_Purpose", "length(btrim(\"Purpose\")) BETWEEN 10 AND 2000");
            });
        });
        model.Entity<LoanItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ItemStatus).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.ReturnCondition).HasMaxLength(16);
            b.HasIndex(x => x.EquipmentTypeId);
            b.ToTable(t => t.HasCheckConstraint("CK_Item_Status", "\"ItemStatus\" IN ('REQUESTED','RESERVED','BORROWED','RETURNED','DAMAGED','LOST','CANCELLED','REJECTED')"));
        });
        model.Entity<LoanHistory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Action).HasMaxLength(32).IsRequired();
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => new { x.LoanRequestId, x.OccurredAt });
        });
    }
}
