using Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure;

// Service-local context: catalog_db only. No cross-service FK; other services reference these IDs logically.
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<EquipmentType> EquipmentTypes => Set<EquipmentType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(150);
            entity.Property(c => c.Description).HasMaxLength(1000);
            entity.HasIndex(c => c.Name).IsUnique();
            entity.HasData(CatalogSeed.Categories);
        });
        modelBuilder.Entity<EquipmentType>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(150);
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.Specifications).HasMaxLength(4000);
            entity.Property(t => t.ImageUrl).HasMaxLength(500);
            entity.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(t => new { t.CategoryId, t.Name }).IsUnique();
            entity.HasIndex(t => new { t.IsActive, t.Name });
            entity.HasData(CatalogSeed.EquipmentTypes);
        });
    }
}
