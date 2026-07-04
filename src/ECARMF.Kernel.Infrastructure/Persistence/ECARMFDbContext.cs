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

    public DbSet<ApprovalRecord> Approvals => Set<ApprovalRecord>();

    public DbSet<ScoreEntry> Scores => Set<ScoreEntry>();

    public DbSet<UserRecord> Users => Set<UserRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(u => u.Identifier).HasMaxLength(400).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(400).IsRequired();
            entity.Property(u => u.Status).HasMaxLength(50).IsRequired();
            entity.Property(u => u.RolesJson).IsRequired();
            entity.HasIndex(u => new { u.TenantId, u.Identifier }).IsUnique();
        });

        modelBuilder.Entity<ScoreEntry>(entity =>
        {
            entity.ToTable("Scores");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(s => s.SubjectType).HasMaxLength(200).IsRequired();
            entity.Property(s => s.SubjectId).HasMaxLength(200).IsRequired();
            entity.Property(s => s.ScoreType).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Value).HasPrecision(18, 6);
            entity.Property(s => s.RuleId).HasMaxLength(200);
            entity.Property(s => s.PackageId).HasMaxLength(200);
            entity.Property(s => s.PackageVersion).HasMaxLength(50);
            entity.HasIndex(s => new { s.TenantId, s.SubjectType, s.SubjectId });
            entity.HasIndex(s => new { s.TenantId, s.ScoreType, s.ComputedAt });
            entity.HasIndex(s => s.CorrelationId);
        });

        modelBuilder.Entity<ApprovalRecord>(entity =>
        {
            entity.ToTable("Approvals");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Approver).HasMaxLength(400).IsRequired();
            entity.Property(a => a.Verdict).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Comment).HasMaxLength(2000);
            // One decision per record: dual approval is not re-votable.
            entity.HasIndex(a => new { a.TenantId, a.TransactionId }).IsUnique();
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Category).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Summary).IsRequired();
            entity.Property(a => a.DetailJson).IsRequired();
            entity.HasIndex(a => new { a.TenantId, a.CorrelationId });
            entity.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });

        modelBuilder.Entity<OutcomeRecord>(entity =>
        {
            entity.ToTable("TransactionOutcomes");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(o => o.EventName).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(o => o.Reason).IsRequired();
            entity.Property(o => o.RuleId).HasMaxLength(200);
            entity.Property(o => o.PackageId).HasMaxLength(200);
            entity.Property(o => o.PackageVersion).HasMaxLength(50);
            entity.HasIndex(o => new { o.TenantId, o.TransactionId });
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(t => t.TransactionType).HasMaxLength(200).IsRequired();
            entity.Property(t => t.SubmittedBy).HasMaxLength(400).IsRequired();
            entity.Property(t => t.PayloadJson).IsRequired();
            entity.HasIndex(t => new { t.TenantId, t.ReceivedAt });
        });

        modelBuilder.Entity<KnowledgePackageRecord>(entity =>
        {
            entity.ToTable("KnowledgePackages");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(p => p.PackageId).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(400).IsRequired();
            entity.Property(p => p.PackageVersion).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Publisher).HasMaxLength(400);
            entity.Property(p => p.Owner).HasMaxLength(400);
            entity.Property(p => p.Status).HasMaxLength(50);
            entity.Property(p => p.ManifestJson).IsRequired();
            entity.HasIndex(p => new { p.TenantId, p.PackageId, p.PackageVersion }).IsUnique();
        });
    }
}
