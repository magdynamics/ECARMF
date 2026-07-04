using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ECARMFDbContext : DbContext
{
    public ECARMFDbContext(DbContextOptions<ECARMFDbContext> options)
        : base(options)
    {
    }

    public DbSet<KnowledgePackageRecord> KnowledgePackages => Set<KnowledgePackageRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<OutcomeRecord> TransactionOutcomes => Set<OutcomeRecord>();

    public DbSet<AuditRecord> AuditEntries => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Category).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Summary).IsRequired();
            entity.Property(a => a.DetailJson).IsRequired();
            entity.HasIndex(a => a.CorrelationId);
            entity.HasIndex(a => a.OccurredAt);
        });

        modelBuilder.Entity<OutcomeRecord>(entity =>
        {
            entity.ToTable("TransactionOutcomes");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.EventName).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(o => o.Reason).IsRequired();
            entity.Property(o => o.RuleId).HasMaxLength(200);
            entity.Property(o => o.PackageId).HasMaxLength(200);
            entity.Property(o => o.PackageVersion).HasMaxLength(50);
            entity.HasIndex(o => o.TransactionId);
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TransactionType).HasMaxLength(200).IsRequired();
            entity.Property(t => t.SubmittedBy).HasMaxLength(400).IsRequired();
            entity.Property(t => t.PayloadJson).IsRequired();
            entity.HasIndex(t => t.ReceivedAt);
        });

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
