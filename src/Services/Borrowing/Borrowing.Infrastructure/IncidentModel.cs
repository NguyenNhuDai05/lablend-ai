using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Borrowing.Infrastructure;

internal static class IncidentModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.HasSequence<long>("IncidentNumber");
        model.Entity<EquipmentIncident>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Ignore(x => x.Closed);
            b.Property(x => x.Code).HasMaxLength(40).IsRequired(); b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.SerialNumber).HasMaxLength(64).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.ProblemCategory).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Resolution).HasMaxLength(2000); b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.StudentId, x.SubmissionKey }).IsUnique();
            b.HasIndex(x => x.LoanItemId).IsUnique().HasFilter("\"Status\" NOT IN ('RESOLVED_REPLACED','RESOLVED_NO_FAULT','RESOLVED_REPAIRED','RESOLVED_NO_REPLACEMENT','CANCELLED')");
            b.HasIndex(x => new { x.Status, x.CreatedAt }); b.HasIndex(x => new { x.EquipmentTypeId, x.CreatedAt });
            b.HasOne<LoanRequest>().WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LoanItem>().WithMany().HasForeignKey(x => x.LoanItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Inspection).WithOne().HasForeignKey<IncidentInspection>(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Replacements).WithOne().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Evidence).WithOne().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => {
                t.HasCheckConstraint("CK_Incident_Description", "length(btrim(\"Description\")) BETWEEN 10 AND 2000");
                t.HasCheckConstraint("CK_Incident_Status", "\"Status\" IN ('REPORTED','INSPECTING','CONFIRMED_DEFECT','WAITING_REPLACEMENT','REPLACEMENT_IN_PROGRESS','RESOLVED_REPLACED','RESOLVED_NO_FAULT','RESOLVED_REPAIRED','RESOLVED_NO_REPLACEMENT','CANCELLED','MANUAL_REVIEW')");
                t.HasCheckConstraint("CK_Incident_Category", "\"ProblemCategory\" IN ('POWER_FAILURE','PHYSICAL_DAMAGE','FUNCTIONAL_ERROR','MISSING_COMPONENT','OTHER')");
            });
        });
        model.Entity<IncidentInspection>(b =>
        {
            b.HasKey(x => x.IncidentId); b.Property(x => x.IncidentId).ValueGeneratedNever();
            b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(24); b.Property(x => x.Notes).HasMaxLength(2000).IsRequired();
            b.Property(x => x.CorrectiveAction).HasMaxLength(2000); b.Property(x => x.VerifiedSerialNumber).HasMaxLength(64);
            b.ToTable(t => t.HasCheckConstraint("CK_Inspection_Physical", "\"Outcome\" = 'LOST' OR (\"PhysicallyReceivedAt\" IS NOT NULL AND \"VerifiedSerialNumber\" IS NOT NULL)"));
        });
        model.Entity<IncidentHistory>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Action).HasMaxLength(40);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.Notes).HasMaxLength(2000); b.HasIndex(x => new { x.IncidentId, x.OccurredAt });
        });
        model.Entity<EquipmentReplacement>(b =>
        {
            b.HasKey(x => x.OperationId); b.Property(x => x.OperationId).ValueGeneratedNever(); b.Property(x => x.SerialNumber).HasMaxLength(64);
            b.HasOne<InventoryOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LoanRequest>().WithMany().HasForeignKey(x => x.ReplacementLoanId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.IncidentId).IsUnique().HasFilter("\"ReplacementAssetUnitId\" IS NOT NULL");
        });
        model.Entity<IncidentEvidence>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.ContentType).HasMaxLength(32);
            b.Property(x => x.ContentHash).HasMaxLength(64); b.HasIndex(x => new { x.IncidentId, x.ContentHash }).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_IncidentEvidence_Size", "octet_length(\"Content\") BETWEEN 1 AND 5242880"));
        });
        model.Entity<LoanItem>(b =>
        {
            b.Property(x => x.AssetSerialNumber).HasMaxLength(64);
            b.HasOne<EquipmentIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LoanItem>().WithMany().HasForeignKey(x => x.ReplacesLoanItemId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
