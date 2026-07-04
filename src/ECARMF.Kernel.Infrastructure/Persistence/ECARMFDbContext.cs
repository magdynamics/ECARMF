using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ECARMFDbContext : DbContext
{
    public ECARMFDbContext(DbContextOptions<ECARMFDbContext> options)
        : base(options)
    {
    }

    public DbSet<KnowledgePackageRecord> KnowledgePackages => Set<KnowledgePackageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgePackageRecord>(entity =>
        {
            entity.ToTable("KnowledgePackages");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.PackageId).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(400).IsRequired();
            entity.Property(p => p.PackageVersion).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Publisher).HasMaxLength(400);
            entity.Property(p => p.Owner).HasMaxLength(400);
            entity.Property(p => p.Status).HasMaxLength(50);
            entity.Property(p => p.ManifestJson).IsRequired();
            entity.HasIndex(p => new { p.PackageId, p.PackageVersion }).IsUnique();
        });
    }
}
