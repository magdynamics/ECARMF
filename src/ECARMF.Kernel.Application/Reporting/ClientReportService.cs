using System.Globalization;
using System.Text;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Notifications;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Application.Reporting;

public interface IClientReportService
{
    /// <summary>Builds the period report for one tenant, archives it in the
    /// document library (immutable, indexed), optionally emails it to the
    /// tenant's executives, and audits the generation.</summary>
    Task<SourceDocument> GenerateAsync(
        string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string requestedBy, bool deliverByEmail, CancellationToken ct = default);

    /// <summary>Monthly cadence, idempotent: for every Active client tenant,
    /// generates (and emails) the previous calendar month's report unless one
    /// is already archived. Returns how many were generated.</summary>
    Task<int> EnsureMonthlyReportsAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// The client deliverable: one self-contained report of what the platform
/// did for the tenant in a period — activity, outcomes, scores, benchmark
/// breaches, alerts, upcoming renewals, open work, the advisor's latest
/// brief, and metered usage. Generated as printable HTML (opens anywhere,
/// prints to PDF), archived in the library like any other evidence, and
/// emailed to the tenant's executives when mail delivery is configured.
/// </summary>
public class ClientReportService : IClientReportService
{
    private readonly ITenantDirectory _tenants;
    private readonly IAuditLog _audit;
    private readonly IUsageMeter _usage;
    private readonly IScoreStore _scores;
    private readonly IDeviationStore _deviations;
    private readonly IBenchmarkStore _benchmarks;
    private readonly IRenewalStore _renewals;
    private readonly ITaskStore _tasks;
    private readonly IAdvisorStore _advisor;
    private readonly IDocumentLibrary _library;
    private readonly IUserStore _users;
    private readonly IMailSettingsStore _mailSettings;
    private readonly IEmailSender _mail;

    public ClientReportService(
        ITenantDirectory tenants, IAuditLog audit, IUsageMeter usage, IScoreStore scores,
        IDeviationStore deviations, IBenchmarkStore benchmarks, IRenewalStore renewals,
        ITaskStore tasks, IAdvisorStore advisor, IDocumentLibrary library,
        IUserStore users, IMailSettingsStore mailSettings, IEmailSender mail)
    {
        _tenants = tenants;
        _audit = audit;
        _usage = usage;
        _scores = scores;
        _deviations = deviations;
        _benchmarks = benchmarks;
        _renewals = renewals;
        _tasks = tasks;
        _advisor = advisor;
        _library = library;
        _users = users;
        _mailSettings = mailSettings;
        _mail = mail;
    }

    public async Task<SourceDocument> GenerateAsync(
        string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string requestedBy, bool deliverByEmail, CancellationToken ct = default)
    {
        var profile = await _tenants.GetAsync(tenantId, ct);
        var tenantName = profile?.Name ?? tenantId;
        var period = $"{periodStart:yyyy-MM-dd} — {periodEnd:yyyy-MM-dd}";

        // What happened: the append-only audit trail is the period's source
        // of truth; everything else is current posture.
        var auditEntries = await _audit.GetByTimeRangeAsync(tenantId, periodStart, periodEnd, ct);
        var activityByCategory = auditEntries
            .GroupBy(a => a.Category)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var usage = await _usage.MeasureAsync(tenantId, periodStart, periodEnd, ct);
        var recentScores = await _scores.GetRecentAsync(tenantId, 500, null, ct);
        var scoreSummary = recentScores
            .GroupBy(s => s.ScoreType)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count(),
                Average = Math.Round(g.Average(s => s.Value), 4),
                Latest = Math.Round(g.OrderByDescending(s => s.ComputedAt).First().Value, 4)
            })
            .OrderBy(s => s.Type)
            .ToList();

        var periodAlerts = (await _deviations.GetRecentAsync(tenantId, 500, ct))
            .Where(d => d.DetectedAt >= periodStart && d.DetectedAt < periodEnd)
            .OrderByDescending(d => d.Severity == "Critical")
            .ThenByDescending(d => d.DetectedAt)
            .ToList();

        var benchmarks = await _benchmarks.GetAllAsync(tenantId, ct);
        var renewals = (await _renewals.GetAllAsync(tenantId, ct))
            .Where(r => r.Status == RenewalStatuses.Active)
            .OrderBy(r => r.DueDate)
            .ToList();
        var horizon = periodEnd.AddDays(60);

        var openTasks = (await _tasks.GetRecentAsync(tenantId, 200, ct))
            .Where(t => t.Status == "Open")
            .OrderByDescending(t => t.Severity == "Critical")
            .ToList();

        var latestBrief = (await _advisor.GetRecentAsync(tenantId, 1, ct)).FirstOrDefault();

        var html = BuildHtml(
            tenantName, tenantId, period, activityByCategory, usage.RecordsProcessed,
            usage.DocumentsArchived, usage.AiCalls, usage.FeedRuns, usage.ActiveUsers,
            scoreSummary.Select(s => (s.Type, s.Count, s.Average, s.Latest)).ToList(),
            periodAlerts.Select(d => (d.Severity, d.MetricType, d.ActualValue, d.ExpectedValue, d.DetectedAt)).ToList(),
            benchmarks.Select(b => (b.Name, b.Severity, b.Enabled)).ToList(),
            renewals.Where(r => r.DueDate <= horizon || r.DueDate < periodEnd)
                .Select(r => (r.Name, r.Category, r.DueDate, r.Counterparty)).ToList(),
            openTasks.Select(t => (t.Severity, t.Title)).ToList(),
            latestBrief?.Title, latestBrief?.ExecutiveSummary);

        var periodKey = $"{periodStart:yyyy-MM}";
        var fileName = $"client-report-{tenantId}-{periodKey}.html";
        var document = await _library.ArchiveAsync(new SourceDocument
        {
            TenantId = tenantId,
            FileName = fileName,
            MediaType = "text",
            SourceId = $"report:{periodKey}",
            SourceCategory = "client-report",
            UploadedBy = requestedBy,
            Metadata = new Dictionary<string, string>
            {
                ["contentType"] = "text/html",
                ["periodStart"] = periodStart.ToString("O"),
                ["periodEnd"] = periodEnd.ToString("O"),
                ["tenantName"] = tenantName
            }
        }, Encoding.UTF8.GetBytes(html), ct);

        string delivery = "not requested";
        if (deliverByEmail)
        {
            delivery = await TryEmailAsync(tenantId, tenantName, period, fileName, html, ct);
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.ReportGenerated,
            Actor = requestedBy,
            Summary = $"Client report generated for {period} ({fileName}); email delivery: {delivery}.",
            Detail = new Dictionary<string, string>
            {
                ["documentId"] = document.Id.ToString(),
                ["period"] = period,
                ["emailDelivery"] = delivery
            }
        }, ct);

        return document;
    }

    public async Task<int> EnsureMonthlyReportsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodStart = monthStart.AddMonths(-1);
        var periodEnd = monthStart;
        var periodKey = $"{periodStart:yyyy-MM}";

        var generated = 0;
        foreach (var tenant in await _tenants.GetAllAsync(ct))
        {
            if (!string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existing = await _library.SearchAsync(
                tenant.TenantId, null, $"report:{periodKey}", null, null, 1, null, ct);
            if (existing.Count > 0)
            {
                continue;
            }

            await GenerateAsync(tenant.TenantId, periodStart, periodEnd,
                "system:flywheel", deliverByEmail: true, ct);
            generated++;
        }

        return generated;
    }

    /// <summary>Report recipients: executives with an email, else the tenant
    /// contact. Absent mail configuration the report still archives — the
    /// outcome string says why nothing was sent.</summary>
    private async Task<string> TryEmailAsync(
        string tenantId, string tenantName, string period, string fileName, string html, CancellationToken ct)
    {
        var settings = await _mailSettings.GetAsync(ct);
        if (settings is null || !settings.Enabled)
        {
            return "skipped: mail delivery not configured/enabled";
        }

        var users = await _users.GetAllAsync(tenantId, ct);
        var recipients = users
            .Where(u => !u.IsSystemActor
                && !string.IsNullOrWhiteSpace(u.Email)
                && string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && u.Roles.Contains("ExecutiveOwner", StringComparer.OrdinalIgnoreCase))
            .Select(u => u.Email!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0)
        {
            var profile = await _tenants.GetAsync(tenantId, ct);
            if (!string.IsNullOrWhiteSpace(profile?.ContactEmail))
            {
                recipients.Add(profile!.ContactEmail!.Trim());
            }
        }
        if (recipients.Count == 0)
        {
            return "skipped: no recipient email on file";
        }

        try
        {
            await _mail.SendAsync(settings, new EmailMessage(
                recipients,
                $"[ECARMF] {tenantName} — report for {period}",
                $"Attached is the {tenantName} activity and risk report for {period}, " +
                "generated by the ECARMF platform. Open it in any browser; print to PDF if needed.",
                new[] { new EmailAttachment(fileName, Encoding.UTF8.GetBytes(html), "text/html") }), ct);
            return $"sent: {string.Join(", ", recipients)}";
        }
        catch (Exception ex)
        {
            return $"failed: {ex.Message}";
        }
    }

    private static string BuildHtml(
        string tenantName, string tenantId, string period,
        IReadOnlyDictionary<string, int> activity,
        int records, int documents, int aiCalls, int feedRuns, int activeUsers,
        IReadOnlyList<(string Type, int Count, decimal Average, decimal Latest)> scores,
        IReadOnlyList<(string Severity, string Metric, decimal Actual, decimal Expected, DateTimeOffset At)> alerts,
        IReadOnlyList<(string Name, string Severity, bool Enabled)> benchmarks,
        IReadOnlyList<(string Name, string Category, DateTimeOffset DueDate, string? Counterparty)> renewals,
        IReadOnlyList<(string Severity, string Title)> openTasks,
        string? briefTitle, string? briefSummary)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{H(tenantName)} — ECARMF report {H(period)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;color:#1c2733;margin:2rem;max-width:900px}");
        sb.AppendLine("h1{border-bottom:3px solid #0288d1;padding-bottom:.3rem}h2{color:#0288d1;margin-top:1.6rem}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:.92rem}");
        sb.AppendLine("th{background:#eef6fb;text-align:left}th,td{border:1px solid #cfd8e0;padding:.35rem .6rem}");
        sb.AppendLine(".kpis{display:flex;gap:1rem;flex-wrap:wrap}.kpi{border:1px solid #cfd8e0;border-radius:8px;padding:.6rem 1.2rem;text-align:center}");
        sb.AppendLine(".kpi b{display:block;font-size:1.5rem;color:#0288d1}");
        sb.AppendLine(".sev-Critical{color:#b71c1c;font-weight:700}.sev-Warning{color:#b26a00;font-weight:600}.sev-Info{color:#546e7a}");
        sb.AppendLine(".muted{color:#607d8b;font-size:.85rem}@media print{body{margin:.5rem}}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>{H(tenantName)}</h1>");
        sb.AppendLine($"<p class=\"muted\">ECARMF platform report · tenant <code>{H(tenantId)}</code> · period {H(period)} · generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");

        sb.AppendLine("<h2>Period at a glance</h2><div class=\"kpis\">");
        sb.AppendLine($"<div class=\"kpi\"><b>{records}</b>records processed</div>");
        sb.AppendLine($"<div class=\"kpi\"><b>{documents}</b>documents archived</div>");
        sb.AppendLine($"<div class=\"kpi\"><b>{alerts.Count}</b>alerts raised</div>");
        sb.AppendLine($"<div class=\"kpi\"><b>{aiCalls}</b>AI consultations</div>");
        sb.AppendLine($"<div class=\"kpi\"><b>{feedRuns}</b>integration feed runs</div>");
        sb.AppendLine($"<div class=\"kpi\"><b>{activeUsers}</b>active users</div></div>");

        if (briefSummary is not null)
        {
            sb.AppendLine($"<h2>Executive advisor</h2><p><strong>{H(briefTitle ?? "Latest brief")}</strong></p>");
            sb.AppendLine($"<p>{H(briefSummary)}</p>");
        }

        sb.AppendLine("<h2>Alerts this period</h2>");
        if (alerts.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No expectations were breached this period.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Severity</th><th>Metric</th><th>Observed</th><th>Expected</th><th>When</th></tr>");
            foreach (var a in alerts)
            {
                sb.AppendLine($"<tr><td class=\"sev-{H(a.Severity)}\">{H(a.Severity)}</td><td>{H(a.Metric)}</td>" +
                    $"<td>{a.Actual.ToString(inv)}</td><td>{a.Expected.ToString(inv)}</td><td>{a.At:yyyy-MM-dd}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Scores &amp; KPIs (current)</h2>");
        if (scores.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No scores computed yet.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Score type</th><th>Observations</th><th>Average</th><th>Latest</th></tr>");
            foreach (var s in scores)
            {
                sb.AppendLine($"<tr><td>{H(s.Type)}</td><td>{s.Count}</td>" +
                    $"<td>{s.Average.ToString(inv)}</td><td>{s.Latest.ToString(inv)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Watched expectations</h2>");
        sb.AppendLine(benchmarks.Count == 0
            ? "<p class=\"muted\">No benchmarks configured.</p>"
            : $"<p>{benchmarks.Count} benchmark(s) configured: " +
              string.Join(", ", benchmarks.Select(b => $"{H(b.Name)} ({H(b.Severity)}{(b.Enabled ? "" : ", disabled")})")) + ".</p>");

        sb.AppendLine("<h2>Renewals &amp; commitments (next 60 days)</h2>");
        if (renewals.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">Nothing coming due in the next 60 days.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Commitment</th><th>Category</th><th>Counterparty</th><th>Due</th></tr>");
            foreach (var r in renewals)
            {
                sb.AppendLine($"<tr><td>{H(r.Name)}</td><td>{H(r.Category)}</td>" +
                    $"<td>{H(r.Counterparty ?? "—")}</td><td>{r.DueDate:yyyy-MM-dd}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Open work</h2>");
        if (openTasks.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No open tasks.</p>");
        }
        else
        {
            sb.AppendLine("<ul>");
            foreach (var t in openTasks.Take(20))
            {
                sb.AppendLine($"<li><span class=\"sev-{H(t.Severity)}\">[{H(t.Severity)}]</span> {H(t.Title)}</li>");
            }
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("<h2>Platform activity detail</h2>");
        if (activity.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No recorded activity this period.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Event</th><th>Count</th></tr>");
            foreach (var (category, count) in activity)
            {
                sb.AppendLine($"<tr><td>{H(category)}</td><td>{count}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p class=\"muted\">Every figure in this report is traceable to the platform's append-only audit trail. " +
            "Original source documents are archived, hashed, and available in the platform Library.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string H(string value) => System.Net.WebUtility.HtmlEncode(value);
}
