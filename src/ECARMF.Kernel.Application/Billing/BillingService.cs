using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Billing;

namespace ECARMF.Kernel.Application.Billing;

/// <summary>Platform-level billing plan definitions.</summary>
public interface IBillingPlanStore
{
    Task<BillingPlan?> GetAsync(string planId, CancellationToken ct = default);
    Task<IReadOnlyList<BillingPlan>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(BillingPlan plan, CancellationToken ct = default);
    Task EnsureDefaultPlanAsync(CancellationToken ct = default);
}

/// <summary>Generated statements per tenant.</summary>
public interface IBillingStatementStore
{
    Task AddAsync(BillingStatement statement, CancellationToken ct = default);
    Task<IReadOnlyList<BillingStatement>> GetForTenantAsync(string tenantId, int limit, CancellationToken ct = default);
}

/// <summary>Measures a tenant's utilization over a period from the data the
/// kernel already records — the meter never keeps its own counters that
/// could drift from the truth.</summary>
public interface IUsageMeter
{
    Task<UsageSummary> MeasureAsync(
        string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct = default);
}

public interface IBillingService
{
    Task<BillingStatement> GenerateStatementAsync(
        string tenantId, string planId, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string generatedBy, CancellationToken ct = default);
}

/// <summary>
/// Charges are utilization x plan rates, line by line, with the usage
/// snapshot embedded in the statement — a client can always see exactly
/// what they are paying for.
/// </summary>
public class BillingService : IBillingService
{
    private readonly IUsageMeter _meter;
    private readonly IBillingPlanStore _plans;
    private readonly IBillingStatementStore _statements;
    private readonly IAuditLog _audit;
    private readonly ISkillCatalog _skills;

    public BillingService(
        IUsageMeter meter, IBillingPlanStore plans, IBillingStatementStore statements,
        IAuditLog audit, ISkillCatalog skills)
    {
        _meter = meter;
        _plans = plans;
        _statements = statements;
        _audit = audit;
        _skills = skills;
    }

    public async Task<BillingStatement> GenerateStatementAsync(
        string tenantId, string planId, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string generatedBy, CancellationToken ct = default)
    {
        var plan = await _plans.GetAsync(planId, ct)
            ?? throw new InvalidOperationException($"Billing plan '{planId}' does not exist.");

        var usage = await _meter.MeasureAsync(tenantId, periodStart, periodEnd, ct);

        var statement = new BillingStatement
        {
            TenantId = tenantId,
            PlanId = plan.PlanId,
            Currency = plan.Currency,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            GeneratedBy = generatedBy
        };

        void AddLine(string metric, decimal quantity, decimal unitPrice)
        {
            if (quantity <= 0 && unitPrice <= 0)
            {
                return;
            }

            statement.Lines.Add(new BillingLineItem
            {
                Metric = metric,
                Quantity = quantity,
                UnitPrice = unitPrice,
                Amount = Math.Round(quantity * unitPrice, 2)
            });
        }

        AddLine("BaseSubscription", 1, plan.BaseMonthlyFee);
        AddLine("RecordsProcessed", usage.RecordsProcessed, plan.PricePerRecord);
        AddLine("DocumentsArchived", usage.DocumentsArchived, plan.PricePerDocumentArchived);
        AddLine("AiCalls", usage.AiCalls, plan.PricePerAiCall);
        AddLine("FeedRuns", usage.FeedRuns, plan.PricePerFeedRun);
        AddLine("ActiveUsers", usage.ActiveUsers, plan.PricePerActiveUser);

        // Per-skill recurring charges: one line for each active priced skill
        // (add-ons and industry skills). Core skills are 0 and add no line.
        foreach (var (name, price) in await _skills.ActivePricedSkillsAsync(tenantId, ct))
        {
            AddLine($"Skill: {name}", 1, price);
        }

        statement.Total = Math.Round(statement.Lines.Sum(l => l.Amount), 2);
        await _statements.AddAsync(statement, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = statement.Id,
            Category = AuditCategories.BillingStatementGenerated,
            Actor = generatedBy,
            Summary = $"Billing statement generated for {periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}: " +
                      $"{statement.Total} {statement.Currency} on plan '{plan.PlanId}'.",
            Detail = new Dictionary<string, string>
            {
                ["statementId"] = statement.Id.ToString(),
                ["planId"] = plan.PlanId,
                ["total"] = statement.Total.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recordsProcessed"] = usage.RecordsProcessed.ToString(),
                ["documentsArchived"] = usage.DocumentsArchived.ToString(),
                ["aiCalls"] = usage.AiCalls.ToString(),
                ["feedRuns"] = usage.FeedRuns.ToString(),
                ["activeUsers"] = usage.ActiveUsers.ToString()
            }
        }, ct);

        return statement;
    }
}
