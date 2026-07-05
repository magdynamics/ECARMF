using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Onboarding;

/// <summary>An industry starter pack: everything a new client of a given
/// kind needs, captured from a tenant the operator already configured well
/// — packages, expectations, and the renewal ladder as relative offsets.</summary>
public class OnboardingTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Description { get; set; }
    public string CreatedFromTenant { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<KnowledgePackageManifest> Packages { get; set; } = [];
    public List<TemplateBenchmark> Benchmarks { get; set; } = [];
    public List<TemplateRenewal> Renewals { get; set; } = [];
}

public record TemplateBenchmark(
    string Name, string? Description, string Kind, string MetricType, string? SubjectId,
    string? RecordType, string? Field, string ExpectationOperator, decimal ExpectedValue,
    string Severity, string NotifyRole, bool CreateTask, bool Enabled);

/// <summary>Renewals carry a relative due date: "the business license is
/// due DueInDays after onboarding" — the client fixes the real date later.</summary>
public record TemplateRenewal(
    string Name, string Category, string? Counterparty, string? Notes,
    int DueInDays, int? RecurrenceMonths, int[] LeadTimeDays, string NotifyRole, bool CreateTask);

public record TemplateSummary(
    string TemplateId, string Name, string? Industry, string? Description,
    string CreatedFromTenant, string CreatedBy, DateTimeOffset CreatedAt,
    int PackageCount, int BenchmarkCount, int RenewalCount);

public record ApplyTemplateResult(
    IReadOnlyList<string> PackagesActivated,
    IReadOnlyList<string> PackagesSkipped,
    int BenchmarksCreated,
    int RenewalsCreated,
    IReadOnlyList<string> Errors);

public interface IOnboardingTemplateStore
{
    Task<IReadOnlyList<OnboardingTemplate>> GetAllAsync(CancellationToken ct = default);
    Task<OnboardingTemplate?> GetAsync(string templateId, CancellationToken ct = default);
    Task UpsertAsync(OnboardingTemplate template, CancellationToken ct = default);
    Task DeleteAsync(string templateId, CancellationToken ct = default);
}

public interface IOnboardingTemplateService
{
    Task<TemplateSummary> CaptureAsync(
        string templateId, string name, string? industry, string? description,
        string fromTenantId, string actor, CancellationToken ct = default);

    Task<ApplyTemplateResult> ApplyAsync(
        string templateId, string toTenantId, string actor, CancellationToken ct = default);

    Task<IReadOnlyList<TemplateSummary>> GetAllAsync(CancellationToken ct = default);

    Task DeleteAsync(string templateId, CancellationToken ct = default);
}

/// <summary>
/// One-click onboarding at scale: configure one restaurant (or realtor, or
/// retailer) well, capture it, and every future client of that kind starts
/// with the same packages, expectations, and renewal ladder — through the
/// same load/activate machinery as manual setup, fully audited. Applying is
/// additive and idempotent: existing packages, benchmarks, and renewals are
/// skipped, never duplicated or overwritten.
/// </summary>
public class OnboardingTemplateService : IOnboardingTemplateService
{
    private readonly IOnboardingTemplateStore _templates;
    private readonly IPackageStore _packages;
    private readonly IPackageLoader _loader;
    private readonly IBenchmarkStore _benchmarks;
    private readonly IRenewalStore _renewals;
    private readonly IAuditLog _audit;

    public OnboardingTemplateService(
        IOnboardingTemplateStore templates, IPackageStore packages, IPackageLoader loader,
        IBenchmarkStore benchmarks, IRenewalStore renewals, IAuditLog audit)
    {
        _templates = templates;
        _packages = packages;
        _loader = loader;
        _benchmarks = benchmarks;
        _renewals = renewals;
        _audit = audit;
    }

    public async Task<TemplateSummary> CaptureAsync(
        string templateId, string name, string? industry, string? description,
        string fromTenantId, string actor, CancellationToken ct = default)
    {
        var activePackages = await _packages.GetByStateAsync(fromTenantId, PackageLoadState.Active, ct);
        var benchmarks = await _benchmarks.GetAllAsync(fromTenantId, ct);
        var renewals = (await _renewals.GetAllAsync(fromTenantId, ct))
            .Where(r => r.Status == RenewalStatuses.Active)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var template = new OnboardingTemplate
        {
            TemplateId = templateId,
            Name = name,
            Industry = industry,
            Description = description,
            CreatedFromTenant = fromTenantId,
            CreatedBy = actor,
            Packages = activePackages.Select(p => p.Manifest).ToList(),
            Benchmarks = benchmarks.Select(b => new TemplateBenchmark(
                b.Name, b.Description, b.Kind, b.MetricType, b.SubjectId, b.RecordType, b.Field,
                b.ExpectationOperator.ToString(), b.ExpectedValue, b.Severity, b.NotifyRole,
                b.CreateTask, b.Enabled)).ToList(),
            Renewals = renewals.Select(r => new TemplateRenewal(
                r.Name, r.Category, r.Counterparty, r.Notes,
                // Relative offset; an already-close (or overdue) source date
                // still gives the new client a sane runway.
                Math.Max(30, (int)Math.Ceiling((r.DueDate - now).TotalDays)),
                r.RecurrenceMonths, r.LeadTimeDays.ToArray(), r.NotifyRole, r.CreateTask)).ToList()
        };

        await _templates.UpsertAsync(template, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = fromTenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.TemplateCaptured,
            Actor = actor,
            Summary = $"Onboarding template '{templateId}' captured from {fromTenantId}: " +
                $"{template.Packages.Count} package(s), {template.Benchmarks.Count} benchmark(s), " +
                $"{template.Renewals.Count} renewal(s).",
            Detail = new Dictionary<string, string>
            {
                ["templateId"] = templateId,
                ["packages"] = string.Join(", ", template.Packages.Select(p => $"{p.PackageId}@{p.PackageVersion}"))
            }
        }, ct);

        return ToSummary(template);
    }

    public async Task<ApplyTemplateResult> ApplyAsync(
        string templateId, string toTenantId, string actor, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(templateId, ct)
            ?? throw new KeyNotFoundException($"Template '{templateId}' does not exist.");

        var activated = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        // Dependency order: a package whose rules trigger on events declared
        // by another package validates only after that package is active.
        foreach (var manifest in OrderByDependencies(template.Packages))
        {
            var reference = $"{manifest.PackageId}@{manifest.PackageVersion}";
            try
            {
                if (await _packages.ExistsAsync(toTenantId, manifest.PackageId, manifest.PackageVersion, ct))
                {
                    skipped.Add(reference);
                    continue;
                }

                // The captured manifest still carries the source tenant's
                // entity identity; each application is a NEW entity in the
                // target tenant, never a shared row.
                manifest.EntityId = Guid.NewGuid();
                manifest.TenantId = toTenantId;

                var loaded = await _loader.LoadAsync(toTenantId, manifest, ct);
                if (!loaded.Success)
                {
                    errors.Add($"{reference}: load failed — {string.Join("; ", loaded.Errors)}");
                    continue;
                }

                var active = await _loader.ActivateAsync(toTenantId, manifest.PackageId, manifest.PackageVersion, ct);
                if (active.Success)
                {
                    activated.Add(reference);
                }
                else
                {
                    errors.Add($"{reference}: activation failed — {string.Join("; ", active.Errors)}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{reference}: {ex.Message}");
            }
        }

        var existingBenchmarks = (await _benchmarks.GetAllAsync(toTenantId, ct))
            .Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var benchmarksCreated = 0;
        foreach (var spec in template.Benchmarks.Where(b => !existingBenchmarks.Contains(b.Name)))
        {
            await _benchmarks.AddAsync(new Benchmark
            {
                TenantId = toTenantId,
                Name = spec.Name,
                Description = spec.Description,
                Kind = spec.Kind,
                MetricType = spec.MetricType,
                SubjectId = spec.SubjectId,
                RecordType = spec.RecordType,
                Field = spec.Field,
                ExpectationOperator = Enum.TryParse<ConditionOperator>(spec.ExpectationOperator, true, out var op)
                    ? op : ConditionOperator.LessOrEqual,
                ExpectedValue = spec.ExpectedValue,
                Severity = spec.Severity,
                NotifyRole = spec.NotifyRole,
                CreateTask = spec.CreateTask,
                Enabled = spec.Enabled,
                CreatedBy = actor
            }, ct);
            benchmarksCreated++;
        }

        var existingRenewals = (await _renewals.GetAllAsync(toTenantId, ct))
            .Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renewalsCreated = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var spec in template.Renewals.Where(r => !existingRenewals.Contains(r.Name)))
        {
            await _renewals.AddAsync(new RenewalCommitment
            {
                TenantId = toTenantId,
                Name = spec.Name,
                Category = spec.Category,
                Counterparty = spec.Counterparty,
                Notes = spec.Notes,
                DueDate = now.AddDays(spec.DueInDays),
                RecurrenceMonths = spec.RecurrenceMonths,
                LeadTimeDays = spec.LeadTimeDays,
                NotifyRole = spec.NotifyRole,
                CreateTask = spec.CreateTask,
                CreatedBy = actor
            }, ct);
            renewalsCreated++;
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = toTenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.TemplateApplied,
            Actor = actor,
            Summary = $"Starter pack '{templateId}' applied: {activated.Count} package(s) activated, " +
                $"{skipped.Count} already present, {benchmarksCreated} benchmark(s), {renewalsCreated} renewal(s)"
                + (errors.Count > 0 ? $", {errors.Count} error(s)." : "."),
            Detail = new Dictionary<string, string>
            {
                ["templateId"] = templateId,
                ["activated"] = string.Join(", ", activated),
                ["skipped"] = string.Join(", ", skipped),
                ["errors"] = string.Join(" | ", errors)
            }
        }, ct);

        return new ApplyTemplateResult(activated, skipped, benchmarksCreated, renewalsCreated, errors);
    }

    public async Task<IReadOnlyList<TemplateSummary>> GetAllAsync(CancellationToken ct = default) =>
        (await _templates.GetAllAsync(ct)).Select(ToSummary).ToList();

    public Task DeleteAsync(string templateId, CancellationToken ct = default) =>
        _templates.DeleteAsync(templateId, ct);

    /// <summary>Topological order over the template's own packages by their
    /// declared dependencies; ties keep capture order. A cycle (invalid
    /// anyway) degrades to capture order rather than failing the apply.</summary>
    public static IReadOnlyList<KnowledgePackageManifest> OrderByDependencies(
        IReadOnlyList<KnowledgePackageManifest> packages)
    {
        var byId = packages.GroupBy(p => p.PackageId).ToDictionary(g => g.Key, g => g.First());
        var ordered = new List<KnowledgePackageManifest>();
        var visited = new Dictionary<string, bool>(); // false = in progress

        void Visit(KnowledgePackageManifest manifest)
        {
            if (visited.TryGetValue(manifest.PackageId, out var done))
            {
                return; // done, or a cycle — either way stop descending
            }

            visited[manifest.PackageId] = false;
            foreach (var dependency in manifest.Dependencies)
            {
                if (byId.TryGetValue(dependency.PackageId, out var dep))
                {
                    Visit(dep);
                }
            }

            visited[manifest.PackageId] = true;
            ordered.Add(manifest);
        }

        foreach (var manifest in packages)
        {
            Visit(manifest);
        }

        return ordered;
    }

    private static TemplateSummary ToSummary(OnboardingTemplate t) => new(
        t.TemplateId, t.Name, t.Industry, t.Description, t.CreatedFromTenant, t.CreatedBy,
        t.CreatedAt, t.Packages.Count, t.Benchmarks.Count, t.Renewals.Count);
}
