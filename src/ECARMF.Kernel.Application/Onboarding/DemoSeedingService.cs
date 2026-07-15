using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
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
    private const int MaxRecords = 250;
    // Even a control-light tenant should show record activity in a demo.
    private const int RecordFloor = 12;

    private readonly ITenantDirectory _tenants;
    private readonly IUserStore _users;
    private readonly IPackageStore _packages;
    private readonly IPackageCatalog _catalog;
    private readonly ITransactionIntakeService _intake;
    private readonly IRenewalStore _renewals;
    private readonly IBenchmarkStore _benchmarks;
    private readonly IAuditLog _audit;

    public DemoSeedingService(
        ITenantDirectory tenants, IUserStore users, IPackageStore packages, IPackageCatalog catalog,
        ITransactionIntakeService intake, IRenewalStore renewals, IBenchmarkStore benchmarks, IAuditLog audit)
    {
        _tenants = tenants;
        _users = users;
        _packages = packages;
        _catalog = catalog;
        _intake = intake;
        _renewals = renewals;
        _benchmarks = benchmarks;
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
            (records, var recordTypes) = await SeedRecordsAsync(demoId, errors, ct);
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

    /// <summary>Submit a demo record for each active rule, crafted to satisfy
    /// the rule's conditions so the control actually fires. Returns the count
    /// and the distinct record types produced.</summary>
    private async Task<(int Count, IReadOnlyList<string> RecordTypes)> SeedRecordsAsync(
        string demoId, List<string> errors, CancellationToken ct)
    {
        var active = await _packages.GetByStateAsync(demoId, PackageLoadState.Active, ct);
        var manifests = active.Select(p => p.Manifest).ToList();
        var rules = manifests.SelectMany(m => m.Rules)
            .Where(r => r.Conditions.Count > 0)
            .Take(MaxRecords)
            .ToList();

        var recordTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        // One record per active rule, crafted to fire the control.
        foreach (var rule in rules)
        {
            var (recordType, payload) = BuildRecord(rule);
            try
            {
                await _intake.ReceiveAsync(new TransactionSubmission(demoId, recordType, "owner@platform", payload), ct);
                recordTypes.Add(recordType);
                count++;
            }
            catch (Exception ex) { errors.Add($"record {rule.RuleId}: {ex.Message}"); }
        }

        // Floor: for control-light tenants, add plausible records per entity
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
                    await _intake.ReceiveAsync(new TransactionSubmission(demoId, entity.EntityTypeName, "owner@platform", payload), ct);
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
        foreach (var rt in recordTypes.Take(2))
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

    /// <summary>A plausible demo record for an entity, one value per attribute
    /// by data type. Populates activity even when no rule targets the type.</summary>
    private static Dictionary<string, string> BuildEntityRecord(EntityDeclaration entity)
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

    private static (string RecordType, Dictionary<string, string> Payload) BuildRecord(RuleDeclaration rule)
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
    private static string SatisfyingValue(RuleCondition c)
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
