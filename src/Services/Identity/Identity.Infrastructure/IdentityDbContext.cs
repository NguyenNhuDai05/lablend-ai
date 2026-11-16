using Microsoft.EntityFrameworkCore;
using Identity.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserAuditEvent> UserAuditEvents => Set<UserAuditEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>(b =>
        {
            b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            b.Property(x => x.Email).HasMaxLength(254).IsRequired();
            b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            b.HasIndex(x => x.NormalizedEmail).IsUnique();
        });
        model.Entity<RefreshToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.UserId, x.TokenFamilyId });
            b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.RevocationReason).HasMaxLength(64);
            b.Property(x => x.Version).IsRowVersion();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Refresh_Expiry", "\"ExpiresAt\" > \"CreatedAt\""));
        });
        model.Entity<UserAuditEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
