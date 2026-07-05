using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Notifications;
using ECARMF.Kernel.Application.Reporting;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Billing;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;
using System.Text;

namespace ECARMF.Kernel.Tests;

public class FakeUsageMeter : IUsageMeter
{
    public Task<UsageSummary> MeasureAsync(
        string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct = default) =>
        Task.FromResult(new UsageSummary(tenantId, periodStart, periodEnd, 42, 5, 1024, 7, 3, 2));
}

/// <summary>The client deliverable: period report built from the audit
/// trail and current posture, archived immutably, emailed to executives,
/// generated monthly exactly once per tenant per period.</summary>
public class ClientReportTests
{
    private const string Tenant = "tenant-a";

    private readonly FakeTenantDirectory _tenants = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly FakeUsageMeter _usage = new();
    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryDeviationStore _deviations = new();
    private readonly InMemoryBenchmarkStore _benchmarks = new();
    private readonly InMemoryRenewalStore _renewals = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryAdvisorStore _advisor = new();
    private readonly InMemoryDocumentLibrary _library = new();
    private readonly InMemoryUserStore _users = new();
    private readonly FakeMailSettingsStore _mail = new();
    private readonly FakeEmailSender _sender = new();
    private readonly ClientReportService _service;

    public ClientReportTests()
    {
        _service = new ClientReportService(
            _tenants, _audit, _usage, _scores, _deviations, _benchmarks,
            _renewals, _tasks, _advisor, _library, _users, _mail, _sender);
        _tenants.Profiles[Tenant] = new TenantProfile
        {
            TenantId = Tenant, Name = "Acme Client", Status = "Active", ContactEmail = "contact@acme.example"
        };
    }

    private (DateTimeOffset Start, DateTimeOffset End) LastMonth()
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (monthStart.AddMonths(-1), monthStart);
    }

    [Fact]
    public async Task Report_archives_html_with_period_content_and_audits()
    {
        var (start, end) = LastMonth();
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = Tenant, Category = AuditCategories.RecordReceived,
            Actor = "u", Summary = "r", OccurredAt = start.AddDays(3)
        });
        _deviations.Items.Add(new DeviationAlert
        {
            TenantId = Tenant, MetricType = "GPPercent", ActualValue = 0.18m, ExpectedValue = 0.25m,
            Severity = "Critical", DetectedAt = start.AddDays(5)
        });
        _scores.Items.Add(new ScoreRecord
        {
            TenantId = Tenant, ScoreType = "GPPercent", SubjectType = "venture", SubjectId = "v1",
            Value = 0.18m, ComputedAt = start.AddDays(5)
        });
        _renewals.Items.Add(new RenewalCommitment
        {
            TenantId = Tenant, Name = "Business license", Category = RenewalCategories.License,
            DueDate = end.AddDays(20), NotifyRole = "ExecutiveOwner", CreatedBy = "u"
        });

        var document = await _service.GenerateAsync(Tenant, start, end, "owner@a", deliverByEmail: false);

        Assert.Equal("client-report", document.SourceCategory);
        Assert.Equal($"report:{start:yyyy-MM}", document.SourceId);
        var html = Encoding.UTF8.GetString(_library.Items.Single().Content);
        Assert.Contains("Acme Client", html);
        Assert.Contains("GPPercent", html);           // score + alert
        Assert.Contains("Business license", html);    // upcoming renewal
        Assert.Contains("RecordReceived", html);      // activity detail
        Assert.Contains("42", html);                  // usage records
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.ReportGenerated);
    }

    [Fact]
    public async Task Report_emails_executives_with_attachment_when_mail_enabled()
    {
        _mail.Settings = new MailDeliverySettings(true, "mail", 25, false, null, null, "a@b.c", "Warning");
        var (start, end) = LastMonth();
        _users.Items.Add(new User
        {
            TenantId = Tenant, Identifier = "owner@a", DisplayName = "Owner",
            Roles = ["ExecutiveOwner"], Email = "owner@acme.example", Status = "Active"
        });

        await _service.GenerateAsync(Tenant, start, end, "owner@a", deliverByEmail: true);

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("owner@acme.example", Assert.Single(sent.To));
        var attachment = Assert.Single(sent.Attachments!);
        Assert.EndsWith(".html", attachment.FileName);
        Assert.Contains("Acme Client", Encoding.UTF8.GetString(attachment.Content));
    }

    [Fact]
    public async Task Report_archives_even_when_mail_is_unconfigured()
    {
        var (start, end) = LastMonth();

        var document = await _service.GenerateAsync(Tenant, start, end, "owner@a", deliverByEmail: true);

        Assert.NotNull(document);
        Assert.Empty(_sender.Sent);
        Assert.Contains(_audit.Items, a =>
            a.Category == AuditCategories.ReportGenerated
            && a.Detail["emailDelivery"].StartsWith("skipped"));
    }

    [Fact]
    public async Task Monthly_cycle_is_idempotent_and_skips_suspended_tenants()
    {
        _tenants.Profiles["tenant-b"] = new TenantProfile
        {
            TenantId = "tenant-b", Name = "Suspended Co", Status = "Suspended"
        };
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(1, await _service.EnsureMonthlyReportsAsync(now)); // tenant-a only
        Assert.Equal(0, await _service.EnsureMonthlyReportsAsync(now)); // already generated
        Assert.Single(_library.Items);
    }
}
