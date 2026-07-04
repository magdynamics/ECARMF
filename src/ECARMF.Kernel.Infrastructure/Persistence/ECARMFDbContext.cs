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

    public DbSet<ConnectorRecord> Connectors => Set<ConnectorRecord>();

    public DbSet<AllocationRecord> Allocations => Set<AllocationRecord>();

    public DbSet<DeviationRecord> Deviations => Set<DeviationRecord>();

    public DbSet<DashboardRecord> Dashboards => Set<DashboardRecord>();

    public DbSet<TaskRecord> Tasks => Set<TaskRecord>();

    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskRecord>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(t => t.WorkflowId).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Title).HasMaxLength(2000).IsRequired();
            entity.Property(t => t.Assignee).HasMaxLength(400).IsRequired();
            entity.Property(t => t.Severity).HasMaxLength(50).IsRequired();
            entity.Property(t => t.Status).HasMaxLength(50).IsRequired();
            entity.Property(t => t.CompletedBy).HasMaxLength(400);
            entity.HasIndex(t => new { t.TenantId, t.Status, t.CreatedAt });
        });

        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(n => n.WorkflowId).HasMaxLength(200).IsRequired();
            entity.Property(n => n.Target).HasMaxLength(400).IsRequired();
            entity.Property(n => n.Message).HasMaxLength(4000).IsRequired();
            entity.Property(n => n.Severity).HasMaxLength(50).IsRequired();
            entity.HasIndex(n => new { n.TenantId, n.CreatedAt });
        });

        modelBuilder.Entity<DashboardRecord>(entity =>
        {
            entity.ToTable("Dashboards");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(d => d.Name).HasMaxLength(200).IsRequired();
            entity.Property(d => d.WidgetsJson).IsRequired();
            entity.HasIndex(d => new { d.TenantId, d.Name }).IsUnique();
        });

        modelBuilder.Entity<DeviationRecord>(entity =>
        {
            entity.ToTable("Deviations");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(d => d.EntityReference).HasMaxLength(400).IsRequired();
            entity.Property(d => d.MetricType).HasMaxLength(100).IsRequired();
            entity.Property(d => d.ExpectedValueSource).HasMaxLength(50).IsRequired();
            entity.Property(d => d.Severity).HasMaxLength(50).IsRequired();
            entity.Property(d => d.ActualValue).HasPrecision(18, 6);
            entity.Property(d => d.ExpectedValue).HasPrecision(18, 6);
            entity.Property(d => d.VarianceMagnitude).HasPrecision(18, 6);
            entity.Property(d => d.ThresholdBreached).HasPrecision(18, 6);
            entity.Property(d => d.AcknowledgedBy).HasMaxLength(400);
            entity.HasIndex(d => new { d.TenantId, d.DetectedAt });
        });

        modelBuilder.Entity<AllocationRecord>(entity =>
        {
            entity.ToTable("Allocations");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.TargetReference).HasMaxLength(400).IsRequired();
            entity.Property(a => a.TargetAssetClass).HasMaxLength(200);
            entity.Property(a => a.RecommendedAmount).HasPrecision(18, 2);
            entity.Property(a => a.ModifiedAmount).HasPrecision(18, 2);
            entity.Property(a => a.ConfidenceScore).HasPrecision(5, 4);
            entity.Property(a => a.TargetInstitution).HasMaxLength(400);
            entity.Property(a => a.TargetJurisdiction).HasMaxLength(100);
            entity.Property(a => a.Tier).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Status).HasMaxLength(50).IsRequired();
            entity.Property(a => a.DecidedBy).HasMaxLength(400);
            entity.HasIndex(a => new { a.TenantId, a.CreatedAt });
        });

        modelBuilder.Entity<ConnectorRecord>(entity =>
        {
            entity.ToTable("Connectors");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(c => c.ConnectorId).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Name).HasMaxLength(400).IsRequired();
            entity.Property(c => c.SourceCategory).HasMaxLength(100).IsRequired();
            entity.Property(c => c.IngestionMode).HasMaxLength(50).IsRequired();
            entity.Property(c => c.SchemaTemplateId).HasMaxLength(200).IsRequired();
            entity.Property(c => c.ReliabilityRating).HasPrecision(5, 4);
            entity.Property(c => c.ProvenanceClass).HasMaxLength(50).IsRequired();
            entity.Property(c => c.Status).HasMaxLength(50).IsRequired();
            entity.HasIndex(c => new { c.TenantId, c.ConnectorId }).IsUnique();
        });

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
