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

    public DbSet<AdvisorBriefRecord> AdvisorBriefs => Set<AdvisorBriefRecord>();

    public DbSet<TenantAiSettingsRecord> TenantAiSettings => Set<TenantAiSettingsRecord>();

    public DbSet<TenantProfileRecord> Tenants => Set<TenantProfileRecord>();

    public DbSet<SourceDocumentRecord> SourceDocuments => Set<SourceDocumentRecord>();

    public DbSet<IntegrationRecord> Integrations => Set<IntegrationRecord>();

    public DbSet<FeedRunRecord> FeedRuns => Set<FeedRunRecord>();

    public DbSet<BenchmarkRecord> Benchmarks => Set<BenchmarkRecord>();

    public DbSet<BillingPlanRecord> BillingPlans => Set<BillingPlanRecord>();

    public DbSet<BillingStatementRecord> BillingStatements => Set<BillingStatementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceDocumentRecord>(entity =>
        {
            entity.ToTable("SourceDocuments");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(d => d.FileName).HasMaxLength(500).IsRequired();
            entity.Property(d => d.MediaType).HasMaxLength(50).IsRequired();
            entity.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(d => d.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(d => d.SourceCategory).HasMaxLength(100).IsRequired();
            entity.Property(d => d.UploadedBy).HasMaxLength(400).IsRequired();
            entity.Property(d => d.ExtractionBackend).HasMaxLength(200);
            entity.Property(d => d.SchemaTemplateId).HasMaxLength(200);
            entity.Property(d => d.RecordIdsJson).IsRequired();
            entity.Property(d => d.MetadataJson).IsRequired();
            entity.HasIndex(d => new { d.TenantId, d.ArchivedAt });
            entity.HasIndex(d => new { d.TenantId, d.SourceId, d.ArchivedAt });
            entity.HasIndex(d => new { d.TenantId, d.Sha256 });
        });

        modelBuilder.Entity<IntegrationRecord>(entity =>
        {
            entity.ToTable("Integrations");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(i => i.IntegrationId).HasMaxLength(200).IsRequired();
            entity.Property(i => i.Name).HasMaxLength(400).IsRequired();
            entity.Property(i => i.ApplicationType).HasMaxLength(100).IsRequired();
            entity.Property(i => i.ConnectorId).HasMaxLength(200).IsRequired();
            entity.Property(i => i.Mode).HasMaxLength(20).IsRequired();
            entity.Property(i => i.PullUrl).HasMaxLength(2000);
            entity.Property(i => i.Status).HasMaxLength(50).IsRequired();
            entity.Property(i => i.CreatedBy).HasMaxLength(400).IsRequired();
            entity.Property(i => i.LastFeedStatus).HasMaxLength(50);
            entity.HasIndex(i => new { i.TenantId, i.IntegrationId }).IsUnique();
        });

        modelBuilder.Entity<FeedRunRecord>(entity =>
        {
            entity.ToTable("FeedRuns");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(r => r.IntegrationId).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Trigger).HasMaxLength(50).IsRequired();
            entity.Property(r => r.TriggeredBy).HasMaxLength(400).IsRequired();
            entity.Property(r => r.Error).HasMaxLength(4000);
            entity.HasIndex(r => new { r.TenantId, r.IntegrationId, r.StartedAt });
        });

        modelBuilder.Entity<BenchmarkRecord>(entity =>
        {
            entity.ToTable("Benchmarks");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(b => b.Name).HasMaxLength(400).IsRequired();
            entity.Property(b => b.Description).HasMaxLength(2000);
            entity.Property(b => b.Kind).HasMaxLength(20).IsRequired();
            entity.Property(b => b.MetricType).HasMaxLength(200).IsRequired();
            entity.Property(b => b.SubjectId).HasMaxLength(200);
            entity.Property(b => b.RecordType).HasMaxLength(200);
            entity.Property(b => b.Field).HasMaxLength(200);
            entity.Property(b => b.ExpectationOperator).HasMaxLength(50).IsRequired();
            entity.Property(b => b.ExpectedValue).HasPrecision(18, 6);
            entity.Property(b => b.Severity).HasMaxLength(50).IsRequired();
            entity.Property(b => b.NotifyRole).HasMaxLength(100).IsRequired();
            entity.Property(b => b.CreatedBy).HasMaxLength(400).IsRequired();
            entity.HasIndex(b => new { b.TenantId, b.Enabled });
        });

        modelBuilder.Entity<BillingPlanRecord>(entity =>
        {
            entity.ToTable("BillingPlans");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.PlanId).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Currency).HasMaxLength(10).IsRequired();
            entity.Property(p => p.BaseMonthlyFee).HasPrecision(18, 2);
            entity.Property(p => p.PricePerRecord).HasPrecision(18, 4);
            entity.Property(p => p.PricePerDocumentArchived).HasPrecision(18, 4);
            entity.Property(p => p.PricePerAiCall).HasPrecision(18, 4);
            entity.Property(p => p.PricePerFeedRun).HasPrecision(18, 4);
            entity.Property(p => p.PricePerActiveUser).HasPrecision(18, 2);
            entity.HasIndex(p => p.PlanId).IsUnique();
        });

        modelBuilder.Entity<BillingStatementRecord>(entity =>
        {
            entity.ToTable("BillingStatements");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(s => s.PlanId).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Currency).HasMaxLength(10).IsRequired();
            entity.Property(s => s.LinesJson).IsRequired();
            entity.Property(s => s.Total).HasPrecision(18, 2);
            entity.Property(s => s.Status).HasMaxLength(50).IsRequired();
            entity.Property(s => s.GeneratedBy).HasMaxLength(400).IsRequired();
            entity.HasIndex(s => new { s.TenantId, s.GeneratedAt });
        });

        modelBuilder.Entity<TenantProfileRecord>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Name).HasMaxLength(400).IsRequired();
            entity.Property(t => t.Industry).HasMaxLength(200);
            entity.Property(t => t.ContactName).HasMaxLength(400);
            entity.Property(t => t.ContactEmail).HasMaxLength(400);
            entity.Property(t => t.Status).HasMaxLength(50).IsRequired();
            entity.Property(t => t.BillingPlanId).HasMaxLength(100);
            entity.Property(t => t.Notes).HasMaxLength(4000);
            entity.Property(t => t.CreatedBy).HasMaxLength(400).IsRequired();
            entity.HasIndex(t => t.TenantId).IsUnique();
        });

        modelBuilder.Entity<TenantAiSettingsRecord>(entity =>
        {
            entity.ToTable("TenantAiSettings");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(s => s.ProtectedApiKey).IsRequired();
            entity.Property(s => s.ApiKeyHint).HasMaxLength(20).IsRequired();
            entity.Property(s => s.Model).HasMaxLength(100);
            entity.Property(s => s.ConfiguredBy).HasMaxLength(400).IsRequired();
            entity.HasIndex(s => s.TenantId).IsUnique();
        });

        modelBuilder.Entity<AdvisorBriefRecord>(entity =>
        {
            entity.ToTable("AdvisorBriefs");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(b => b.Title).HasMaxLength(400).IsRequired();
            entity.Property(b => b.ExecutiveSummary).IsRequired();
            entity.Property(b => b.RecommendationsJson).IsRequired();
            entity.Property(b => b.ModelReference).HasMaxLength(200).IsRequired();
            entity.Property(b => b.Provenance).HasMaxLength(50).IsRequired();
            entity.Property(b => b.RequestedBy).HasMaxLength(400).IsRequired();
            entity.Property(b => b.FeedbackBy).HasMaxLength(400);
            entity.HasIndex(b => new { b.TenantId, b.CreatedAt });
        });

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
            entity.Property(u => u.Email).HasMaxLength(400);
            entity.Property(u => u.Phone).HasMaxLength(100);
            entity.Property(u => u.JobTitle).HasMaxLength(200);
            entity.Property(u => u.AccessKeyHash).HasMaxLength(100);
            entity.HasIndex(u => new { u.TenantId, u.Identifier }).IsUnique();
            entity.HasIndex(u => u.AccessKeyHash).IsUnique().HasFilter("[AccessKeyHash] IS NOT NULL");
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
