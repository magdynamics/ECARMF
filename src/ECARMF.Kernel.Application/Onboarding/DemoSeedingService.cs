using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Cases;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Cases;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Application.Onboarding;

public sealed record DemoSeedResult(
    string DemoTenantId,
    bool Created,
    int SkillsInstalled,
    int RecordsSubmitted,
    int RenewalsCreated,
    int BenchmarksCreated,
    IReadOnlyList<string> Errors);

public interface IDemoSeedingService
{
    /// <summary>Create (or top up) a demo twin of a tenant: clone its branding,
    /// install its active skills, and generate demo data that exercises the
    /// system — records that fire each control, plus renewals and benchmarks.</summary>
    Task<DemoSeedResult> SeedAsync(string sourceTenantId, string actor, CancellationToken ct = default);
}

/// <summary>
/// The demo/training twin factory. For a real tenant it stands up
/// "{tenant}-demo" with the same capabilities and enough synthetic data to
/// light up every screen — so the platform can be demonstrated at full
/// capability without touching a client's real data. Everything is built
/// through the same audited paths as production (catalog install, record
/// intake), never by writing rows directly.
/// </summary>
public class DemoSeedingService : IDemoSeedingService
{
    public const string DemoSuffix = "-demo";
    // Heavy demo data: enough to make dashboards, the risk heatmap, and the
    // health board look real.
    private const int MaxRecords = 2500;
    private const int RuleRepeat = 3;      // records per control (volume)
    private const int KpiRecords = 16;     // records per KPI (a spread of values)
    private const int RecordFloor = 12;    // minimum activity for control-light tenants

    private readonly ITenantDirectory _tenants;
    private readonly IUserStore _users;
    private readonly IPackageStore _packages;
    private readonly IPackageCatalog _catalog;
    private readonly ITransactionIntakeService _intake;
    private readonly IRenewalStore _renewals;
    private readonly IBenchmarkStore _benchmarks;
    private readonly ICaseStore _cases;
    private readonly IAuditLog _audit;

    public DemoSeedingService(
        ITenantDirectory tenants, IUserStore users, IPackageStore packages, IPackageCatalog catalog,
        ITransactionIntakeService intake, IRenewalStore renewals, IBenchmarkStore benchmarks,
        ICaseStore cases, IAuditLog audit)
    {
        _tenants = tenants;
        _users = users;
        _packages = packages;
        _catalog = catalog;
        _intake = intake;
        _renewals = renewals;
        _benchmarks = benchmarks;
        _cases = cases;
        _audit = audit;
    }

    public async Task<DemoSeedResult> SeedAsync(string sourceTenantId, string actor, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var source = await _tenants.GetAsync(sourceTenantId, ct)
            ?? throw new KeyNotFoundException($"Tenant '{sourceTenantId}' is not onboarded.");
        if (PlatformTenant.IsPlatform(sourceTenantId))
            throw new InvalidOperationException("The operator tenant has no demo twin.");
        if (sourceTenantId.EndsWith(DemoSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{sourceTenantId}' is already a demo tenant.");

        var demoId = sourceTenantId + DemoSuffix;
        var created = await _tenants.GetAsync(demoId, ct) is null;

        if (created)
        {
            // Cap sensitivity so the demo stays reachable in header/demo mode
            // (Regulated tenants refuse header identity) while keeping the PHI
            // flag so masking still demonstrates.
            var tier = SensitivityTiers.AtLeast(source.SensitivityTier, SensitivityTiers.HighSensitivity)
                ? SensitivityTiers.Elevated
                : source.SensitivityTier;

            await _tenants.AddAsync(new TenantProfile
            {
                TenantId = demoId,
                Name = source.Name + " (Demo)",
                Industry = source.Industry,
                SensitivityTier = tier,
                Brand = (string.IsNullOrWhiteSpace(source.Brand) ? source.Name : source.Brand) + " Demo",
                Segment = source.Segment,
                AccentColor = source.AccentColor,
                HandlesPhi = source.HandlesPhi,
                Notes = $"Demo twin of {sourceTenantId}",
                CreatedBy = actor
            }, ct);
            await _users.EnsureSeedUsersAsync(demoId, ct);
        }

        // Install the source's active packages (skills) into the demo.
        var sourceActive = (await _packages.GetByStateAsync(sourceTenantId, PackageLoadState.Active, ct))
            .Select(p => (p.Manifest.PackageId, p.Manifest.PackageVersion))
            .DistinctBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skillsInstalled = 0;
        foreach (var (packageId, version) in sourceActive)
        {
            try
            {
                var r = await _catalog.InstallAsync(packageId, version, demoId, actor, withDependencies: true, ct);
                skillsInstalled += r.Activated.Count;
                errors.AddRange(r.Errors);
            }
            catch (Exception ex) { errors.Add($"{packageId}: {ex.Message}"); }
        }

        // Only generate data on a fresh twin, so re-running doesn't pile up.
        var records = 0; var renewals = 0; var benchmarks = 0;
        if (created)
        {
            var caseIds = await SeedCasesAsync(demoId, actor, ct);
            (records, var recordTypes) = await SeedRecordsAsync(demoId, caseIds, errors, ct);
            renewals = await SeedRenewalsAsync(demoId, actor, ct);
            benchmarks = await SeedBenchmarksAsync(demoId, recordTypes, actor, ct);
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = demoId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.DemoTenantSeeded,
            Actor = actor,
            Summary = $"Demo twin of '{sourceTenantId}' {(created ? "created" : "topped up")}: "
                + $"{skillsInstalled} skill(s), {records} record(s), {renewals} renewal(s), {benchmarks} benchmark(s)"
                + (errors.Count > 0 ? $", {errors.Count} error(s)." : "."),
            Detail = new Dictionary<string, string>
            {
                ["source"] = sourceTenantId,
                ["skills"] = skillsInstalled.ToString(),
                ["records"] = records.ToString()
            }
        }, ct);

        return new DemoSeedResult(demoId, created, skillsInstalled, records, renewals, benchmarks, errors);
    }

    /// <summary>A few demo cases/projects to file records under, so the Cases
    /// screen has something to compare.</summary>
    private async Task<IReadOnlyList<string>> SeedCasesAsync(string demoId, string actor, CancellationToken ct)
    {
        var specs = new (string Id, string Name, string Desc)[]
        {
            ("onboarding-review", "Onboarding review", "New client/account onboarding and its checks."),
            ("quarterly-audit", "Quarterly audit", "The current quarter's audit and control testing."),
            ("incident-response", "Incident response", "Records tied to an open operational incident."),
        };
        var ids = new List<string>();
        foreach (var s in specs)
        {
            await _cases.AddAsync(new Case
            {
                TenantId = demoId, CaseId = s.Id, Name = s.Name, Description = s.Desc, CreatedBy = actor
            }, ct);
            ids.Add(s.Id);
        }
        return ids;
    }

    private async Task<(int Count, IReadOnlyList<string> RecordTypes)> SeedRecordsAsync(
        string demoId, IReadOnlyList<string> caseIds, List<string> errors, CancellationToken ct)
    {
        var active = await _packages.GetByStateAsync(demoId, PackageLoadState.Active, ct);
        var manifests = active.Select(p => p.Manifest).ToList();

        var recordTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        var seq = 0; // drives the backdate spread so records span past periods

        // 1. Several records per active rule, crafted to fire the control.
        foreach (var rule in manifests.SelectMany(m => m.Rules).Where(r => r.Conditions.Count > 0))
        {
            if (count >= MaxRecords) break;
            var (recordType, payload) = BuildRecord(rule);
            for (var i = 0; i < RuleRepeat && count < MaxRecords; i++)
            {
                try
                {
                    await _intake.ReceiveAsync(new TransactionSubmission(demoId, recordType, "owner@platform", payload, Spread(seq), CaseIdFor(seq++, caseIds)), ct);
                    recordTypes.Add(recordType);
                    count++;
                }
                catch (Exception ex) { errors.Add($"record {rule.RuleId}: {ex.Message}"); break; }
            }
        }

        // 2. KPI-targeted records with numeric payloads and a spread of values,
        // so KPIs emit scores — this is what fills the dashboard, risk heatmap,
        // and health board.
        var kpis = manifests.SelectMany(m => m.PerformanceFrameworks).SelectMany(f => f.Kpis)
            .Where(k => !string.IsNullOrWhiteSpace(k.TriggerRecordType) && !string.IsNullOrWhiteSpace(k.Formula));
        foreach (var kpi in kpis)
        {
            for (var i = 0; i < KpiRecords && count < MaxRecords; i++)
            {
                var payload = BuildKpiRecord(kpi, i);
                try
                {
                    await _intake.ReceiveAsync(new TransactionSubmission(demoId, kpi.TriggerRecordType, "owner@platform", payload, Spread(seq), CaseIdFor(seq++, caseIds)), ct);
                    recordTypes.Add(kpi.TriggerRecordType);
                    count++;
                }
                catch (Exception ex) { errors.Add($"kpi {kpi.KpiId}: {ex.Message}"); break; }
            }
        }

        // 3. Floor: for control-light tenants, add plausible records per entity
        // type so every demo has visible activity (these may not fire a rule).
        if (count < RecordFloor)
        {
            var entities = manifests.SelectMany(m => m.Entities)
                .DistinctBy(e => e.EntityTypeName, StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                if (count >= RecordFloor) break;
                var payload = BuildEntityRecord(entity);
                try
                {
                    await _intake.ReceiveAsync(new TransactionSubmission(demoId, entity.EntityTypeName, "owner@platform", payload, Spread(seq), CaseIdFor(seq++, caseIds)), ct);
                    recordTypes.Add(entity.EntityTypeName);
                    count++;
                }
                catch (Exception ex) { errors.Add($"record {entity.EntityTypeName}: {ex.Message}"); }
            }
        }

        return (count, recordTypes.ToList());
    }

    private async Task<int> SeedRenewalsAsync(string demoId, string actor, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var specs = new (string Name, string Category, int DueInDays)[]
        {
            ("Business license renewal", "License", 45),
            ("Annual compliance filing", "Regulatory", 90),
            ("Professional liability insurance", "Insurance", 20),
            ("Data protection audit", "Compliance", 120),
            ("Vendor contract renewal", "Contract", 60),
            ("Cyber insurance renewal", "Insurance", 200),
        };
        var made = 0;
        foreach (var s in specs)
        {
            await _renewals.AddAsync(new RenewalCommitment
            {
                TenantId = demoId, Name = s.Name, Category = s.Category,
                DueDate = now.AddDays(s.DueInDays), RecurrenceMonths = 12,
                LeadTimeDays = [30, 7], NotifyRole = "ExecutiveOwner", CreateTask = true,
                CreatedBy = actor
            }, ct);
            made++;
        }
        return made;
    }

    private async Task<int> SeedBenchmarksAsync(
        string demoId, IReadOnlyList<string> recordTypes, string actor, CancellationToken ct)
    {
        if (recordTypes.Count == 0) return 0;
        var made = 0;
        foreach (var rt in recordTypes.Take(4))
        {
            await _benchmarks.AddAsync(new Benchmark
            {
                TenantId = demoId,
                Name = $"{rt} volume ceiling",
                Description = $"Demo benchmark on {rt} records.",
                Kind = "Operational", MetricType = "Count", RecordType = rt,
                ExpectationOperator = ConditionOperator.LessOrEqual, ExpectedValue = 1000,
                Severity = "Medium", NotifyRole = "ExecutiveOwner", CreateTask = false, Enabled = true,
                CreatedBy = actor
            }, ct);
            made++;
        }
        return made;
    }

    /// <summary>A received-timestamp spread across ~4 months so demo records
    /// populate several periods for period-over-period comparison.</summary>
    internal static DateTimeOffset Spread(int seq) =>
        DateTimeOffset.UtcNow.AddDays(-((seq * 17) % 118)).AddHours(-(seq % 24));

    /// <summary>File about three-quarters of demo records under a rotating case
    /// so cases have records to compare; the rest stay uncased.</summary>
    internal static string? CaseIdFor(int seq, IReadOnlyList<string> caseIds) =>
        caseIds.Count == 0 || seq % 4 == 0 ? null : caseIds[seq % caseIds.Count];

    private static readonly string[] RiskCategories =
        ["Credit", "Market", "Operational", "Compliance", "Liquidity"];

    private static readonly System.Text.RegularExpressions.Regex Identifier =
        new("[A-Za-z_][A-Za-z0-9_]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A record that makes a KPI compute: recordType matches the KPI's
    /// trigger, every field its formula references is a number, and severity/
    /// likelihood-style fields spread 1..5 so the risk heatmap fills across
    /// cells. Index i varies the values so scores form a real distribution.</summary>
    internal static Dictionary<string, string> BuildKpiRecord(KPIDefinition kpi, int i)
    {
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["recordType"] = kpi.TriggerRecordType
        };

        // Every identifier the formula reads must resolve to a number.
        foreach (System.Text.RegularExpressions.Match m in Identifier.Matches(kpi.Formula))
            payload[m.Value] = NumberFor(m.Value, i);

        // Context fields the KPI carries onto its score (e.g. severity/likelihood).
        foreach (var mf in kpi.MetadataFields)
            payload[mf] = NumberFor(mf, i);

        // Populate the riskType token field (e.g. {category}) with a rotating
        // category, so risk KPIs actually land on the risk heatmap (which skips
        // scores with no riskType) and spread across several groups.
        if (!string.IsNullOrWhiteSpace(kpi.RiskType)
            && kpi.RiskType.StartsWith('{') && kpi.RiskType.EndsWith('}'))
        {
            var field = kpi.RiskType[1..^1];
            payload[field] = RiskCategories[i % RiskCategories.Length];
        }

        // Spread scores across a handful of subjects so the boards look populated.
        if (!string.IsNullOrWhiteSpace(kpi.SubjectField))
            payload[kpi.SubjectField] = $"Unit-{(i % 4) + 1}";

        return payload;
    }

    internal static string NumberFor(string field, int i)
    {
        var f = field.ToLowerInvariant();
        // Severity/likelihood-style fields ride the 1..5 scale for the heatmap.
        if (f.Contains("sever") || f.Contains("likel") || f.Contains("impact") || f.Contains("probab"))
            return ((i % 5) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Otherwise a varied but bounded value so the KPI trend looks real.
        return (50 + (i * 37 % 450)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A plausible demo record for an entity, one value per attribute
    /// by data type. Populates activity even when no rule targets the type.</summary>
    internal static Dictionary<string, string> BuildEntityRecord(EntityDeclaration entity)
    {
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["recordType"] = entity.EntityTypeName
        };
        foreach (var a in entity.Attributes)
        {
            var t = a.DataType.ToLowerInvariant();
            payload[a.Name] =
                t.Contains("int") || t.Contains("num") || t.Contains("decimal") || t.Contains("money") ? "100"
                : t.Contains("bool") ? "true"
                : t.Contains("date") || t.Contains("time") ? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
                : "demo";
        }
        return payload;
    }

    internal static (string RecordType, Dictionary<string, string> Payload) BuildRecord(RuleDeclaration rule)
    {
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? recordType = null;
        foreach (var c in rule.Conditions)
        {
            var value = SatisfyingValue(c);
            payload[c.Field] = value;
            if (string.Equals(c.Field, "recordType", StringComparison.OrdinalIgnoreCase)
                && c.Operator == ConditionOperator.Equals)
                recordType = value;
        }
        recordType ??= payload.TryGetValue("recordType", out var rt) ? rt : rule.RuleId;
        payload["recordType"] = recordType;
        return (recordType, payload);
    }

    /// <summary>A field value that satisfies a single condition, so a record
    /// built from all of a rule's conditions triggers it.</summary>
    internal static string SatisfyingValue(RuleCondition c)
    {
        var isNum = decimal.TryParse(c.Value, out var d);
        return c.Operator switch
        {
            ConditionOperator.Equals => c.Value,
            ConditionOperator.Contains => c.Value,
            ConditionOperator.NotEquals => c.Value + "-demo",
            ConditionOperator.GreaterThan => isNum ? (d + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : c.Value + "1",
            ConditionOperator.GreaterOrEqual => isNum ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : c.Value,
            ConditionOperator.LessThan => isNum ? (d - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
            ConditionOperator.LessOrEqual => isNum ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : c.Value,
            _ => c.Value
        };
    }
}
