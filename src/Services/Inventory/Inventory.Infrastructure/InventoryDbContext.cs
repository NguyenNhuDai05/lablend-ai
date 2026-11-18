using Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

// Service-local context: inventory_db only. EquipmentTypeId/LoanId/LoanItemId are logical references (no cross-DB FK).
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    private const string ActiveReservation = "\"Status\" IN ('HELD', 'HANDED_OVER')";

    public DbSet<AssetUnit> AssetUnits => Set<AssetUnit>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReserveOperation> ReserveOperations => Set<ReserveOperation>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ReplacementOperation> ReplacementOperations => Set<ReplacementOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReplacementOperation>(b =>
        {
            b.HasKey(x => x.OperationId); b.Property(x => x.OperationId).ValueGeneratedNever();
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); b.Property(x => x.ResultJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<AssetUnit>(entity =>
        {
            entity.Property(a => a.SerialNumber).HasMaxLength(64);
            entity.Property(a => a.Location).HasMaxLength(150);
            entity.Property(a => a.Notes).HasMaxLength(1000);
            entity.Property(a => a.Status).HasMaxLength(20);
            // xmin: admin edits fail with 409 instead of overwriting a concurrent reservation.
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(a => a.SerialNumber).IsUnique();
            entity.HasIndex(a => new { a.EquipmentTypeId, a.Status });
            entity.ToTable(t => t.HasCheckConstraint("CK_AssetUnits_Status",
                "\"Status\" IN ('AVAILABLE', 'RESERVED', 'ON_LOAN', 'MAINTENANCE', 'LOST', 'RETIRED')"));
            entity.HasData(InventorySeed.AssetUnits);
            // New movements added to AssetUnit.Movements are inserted on SaveChanges (Id unset → Added).
            entity.HasMany(a => a.Movements).WithOne().HasForeignKey(m => m.AssetUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.Property(m => m.FromStatus).HasMaxLength(20);
            entity.Property(m => m.ToStatus).HasMaxLength(20);
        });

        modelBuilder.Entity<MaintenanceRecord>(entity =>
        {
            entity.Property(m => m.Reason).HasMaxLength(1000);
            entity.Property(m => m.CompletionNotes).HasMaxLength(1000);
            entity.HasOne<AssetUnit>().WithMany().HasForeignKey(m => m.AssetUnitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(m => m.AssetUnitId, "UX_MaintenanceRecords_Open").IsUnique().HasFilter("\"CompletedAt\" IS NULL");
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.Property(r => r.Status).HasMaxLength(20);
            entity.Property(r => r.ReturnCondition).HasMaxLength(20);
            entity.HasOne(r => r.AssetUnit).WithMany().HasForeignKey(r => r.AssetUnitId).OnDelete(DeleteBehavior.Restrict);
            // A unit, and a loan item, can be held by at most one active reservation.
            entity.HasIndex(r => r.AssetUnitId, "UX_Reservations_ActiveAsset").IsUnique().HasFilter(ActiveReservation);
            entity.HasIndex(r => r.LoanItemId, "UX_Reservations_ActiveLoanItem").IsUnique().HasFilter(ActiveReservation);
            entity.HasIndex(r => r.LoanId);
            entity.HasIndex(r => r.OperationId);
            entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_Status",
                "\"Status\" IN ('HELD', 'HANDED_OVER', 'RETURNED', 'RELEASED')"));
        });

        modelBuilder.Entity<ReserveOperation>(entity =>
        {
            entity.HasKey(o => o.OperationId);
            entity.Property(o => o.RequestHash).HasMaxLength(64);
        });
    }
}
