using Microsoft.EntityFrameworkCore;
using Borrowing.Domain;

namespace Borrowing.Infrastructure;

public sealed class BorrowingDbContext(DbContextOptions<BorrowingDbContext> options) : DbContext(options)
{
    public DbSet<LoanRequest> LoanRequests => Set<LoanRequest>();
    public DbSet<LoanItem> LoanItems => Set<LoanItem>();
    public DbSet<LoanHistory> LoanHistories => Set<LoanHistory>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LoanRequest>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Purpose).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.RejectionReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.StudentId, x.CreatedAt });
            b.HasIndex(x => new { x.Status, x.CreatedAt });
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
