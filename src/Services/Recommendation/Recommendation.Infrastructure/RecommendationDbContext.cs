using Microsoft.EntityFrameworkCore;
using Recommendation.Domain;

namespace Recommendation.Infrastructure;

public sealed class RecommendationDbContext(DbContextOptions<RecommendationDbContext> options) : DbContext(options)
{
    public DbSet<RecommendationSession> Sessions => Set<RecommendationSession>();
    public DbSet<RecommendationItem> Items => Set<RecommendationItem>();
    public DbSet<RecommendationDailyUsage> DailyUsage => Set<RecommendationDailyUsage>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var s = model.Entity<RecommendationSession>();
        s.ToTable("RecommendationSessions", t => t.HasCheckConstraint("CK_Session_Purpose", "length(\"Purpose\") BETWEEN 10 AND 2000"));
        s.HasKey(x => x.Id); s.Property(x => x.Purpose).HasMaxLength(2000).IsRequired();
        s.Property(x => x.ModelUsed).HasMaxLength(100).IsRequired();
        s.Property(x => x.Constraints).HasColumnType("text[]").IsRequired();
        s.HasIndex(x => new { x.UserId, x.CreatedAt });
        s.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        var i = model.Entity<RecommendationItem>();
        i.ToTable("RecommendationItems", t => {
            t.HasCheckConstraint("CK_Item_Rank", "\"Rank\" BETWEEN 1 AND 6");
            t.HasCheckConstraint("CK_Item_Text", "length(btrim(\"Reason\")) > 0 AND length(btrim(\"UsageNotes\")) > 0");
        });
        i.HasKey(x => x.Id); i.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); i.Property(x => x.UsageNotes).HasMaxLength(2000).IsRequired();
        i.HasIndex(x => x.SessionId); i.HasIndex(x => new { x.SessionId, x.EquipmentTypeId }).IsUnique();
        i.HasIndex(x => new { x.SessionId, x.Rank }).IsUnique();
        var u = model.Entity<RecommendationDailyUsage>(); u.ToTable("RecommendationDailyUsage");
        u.HasKey(x => new { x.UserId, x.Day });
    }
}

public sealed class RecommendationDailyUsage
{
    public Guid UserId { get; set; }
    public DateOnly Day { get; set; }
    public int Attempts { get; set; }
}
