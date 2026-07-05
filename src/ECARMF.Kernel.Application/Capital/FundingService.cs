using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Capital;

public interface IFundingSourceStore
{
    Task<FundingSource?> GetAsync(string tenantId, string sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<FundingSource>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(FundingSource source, CancellationToken ct = default);
    Task UpdateAsync(FundingSource source, CancellationToken ct = default);
}

public interface IFundingEventStore
{
    Task<FundingEvent?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FundingEvent>> GetBySourceAsync(string tenantId, Guid fundingSourceId, CancellationToken ct = default);
    Task AddAsync(FundingEvent fundingEvent, CancellationToken ct = default);
    Task UpdateAsync(FundingEvent fundingEvent, CancellationToken ct = default);
}

public interface IFundingService
{
    Task<FundingSource> CreateSourceAsync(FundingSource source, string actor, CancellationToken ct = default);

    Task<FundingEvent> RequestEventAsync(
        string tenantId, string sourceId, FundingEvent request, string actor, CancellationToken ct = default);

    /// <summary>Human decision on a requested event. A system/AI actor can
    /// never approve capital movement — same segregation as CapitalFlow.</summary>
    Task<FundingEvent> DecideAsync(
        string tenantId, Guid eventId, User decider, bool approve, string? comment, CancellationToken ct = default);

    Task<FundingEvent> MarkDisbursedAsync(
        string tenantId, Guid eventId, string actor, CancellationToken ct = default);
}

/// <summary>
/// Inbound capital (Rosetta Requirement 4): the request → human decision →
/// disbursement chain for lender draws and investor contributions, with the
/// same audit discipline as every other financial event in the kernel.
/// </summary>
public class FundingService : IFundingService
{
    private readonly IFundingSourceStore _sources;
    private readonly IFundingEventStore _events;
    private readonly IAuditLog _audit;
    private readonly INotificationStore _notifications;

    public FundingService(
        IFundingSourceStore sources, IFundingEventStore events,
        IAuditLog audit, INotificationStore notifications)
    {
        _sources = sources;
        _events = events;
        _audit = audit;
        _notifications = notifications;
    }

    public async Task<FundingSource> CreateSourceAsync(
        FundingSource source, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source.SourceId) || string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("sourceId and name are required.");
        if (source.Kind is not (FundingSourceKinds.Debt or FundingSourceKinds.Equity))
            throw new ArgumentException("kind must be Debt or Equity.");
        if (await _sources.GetAsync(source.TenantId, source.SourceId, ct) is not null)
            throw new ArgumentException($"Funding source '{source.SourceId}' already exists.");

        source.SourceId = source.SourceId.Trim().ToLowerInvariant();
        source.CreatedBy = actor;
        await _sources.AddAsync(source, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = source.TenantId,
            CorrelationId = source.Id,
            Category = AuditCategories.FundingSourceCreated,
            Actor = actor,
            Summary = $"Funding source '{source.Name}' ({source.Kind}) registered for unit '{source.UnitId}'"
                + (source.CommitmentAmount is { } c ? $", commitment {c:N0}." : "."),
            Detail = new Dictionary<string, string>
            {
                ["sourceId"] = source.SourceId,
                ["unitId"] = source.UnitId,
                ["kind"] = source.Kind,
                ["institution"] = source.Institution ?? "",
                ["commitment"] = source.CommitmentAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
            }
        }, ct);

        return source;
    }

    public async Task<FundingEvent> RequestEventAsync(
        string tenantId, string sourceId, FundingEvent request, string actor, CancellationToken ct = default)
    {
        var source = await _sources.GetAsync(tenantId, sourceId, ct)
            ?? throw new KeyNotFoundException($"Funding source '{sourceId}' does not exist.");
        if (!FundingEventTypes.All.Contains(request.EventType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("eventType must be one of: " + string.Join(", ", FundingEventTypes.All));
        if (request.Amount <= 0)
            throw new ArgumentException("amount must be positive.");
        if (string.Equals(request.EventType, FundingEventTypes.Draw, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.MilestoneReference))
            throw new ArgumentException("A lender draw must reference the milestone it is claimed against.");

        request.TenantId = tenantId;
        request.FundingSourceId = source.Id;
        request.EventType = FundingEventTypes.All.First(t =>
            string.Equals(t, request.EventType, StringComparison.OrdinalIgnoreCase));
        request.Status = FundingEventStatuses.Requested;
        request.RequestedBy = actor;
        await _events.AddAsync(request, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = request.Id,
            Category = AuditCategories.FundingEventRequested,
            Actor = actor,
            Summary = $"{request.EventType} of {request.Amount:N0} requested from '{source.Name}'"
                + (request.MilestoneReference is { } m ? $" against milestone '{m}'" : "")
                + (request.PercentCompleteClaimed is { } p ? $" ({p:P0} complete claimed)" : "") + ".",
            Detail = new Dictionary<string, string>
            {
                ["fundingSourceId"] = source.SourceId,
                ["eventType"] = request.EventType,
                ["amount"] = request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["milestone"] = request.MilestoneReference ?? "",
                ["percentClaimed"] = request.PercentCompleteClaimed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ["documentation"] = request.DocumentationReference ?? "",
                ["verification"] = request.VerificationNote ?? ""
            }
        }, ct);

        await _notifications.AddAsync(new NotificationItem
        {
            TenantId = tenantId,
            WorkflowId = $"funding:{source.SourceId}",
            Target = "TreasuryOfficer",
            Message = $"{request.EventType} of {request.Amount:N0} from '{source.Name}' awaits decision"
                + (request.MilestoneReference is { } mr
                    ? $" — claimed against '{mr}'; verify against operational completion data before approving."
                    : "."),
            Severity = "Info",
            CorrelationId = request.Id
        }, ct);

        return request;
    }

    public async Task<FundingEvent> DecideAsync(
        string tenantId, Guid eventId, User decider, bool approve, string? comment, CancellationToken ct = default)
    {
        if (decider.IsSystemActor)
            throw new InvalidOperationException("An AI/system actor cannot decide funding events.");

        var fundingEvent = await _events.GetAsync(tenantId, eventId, ct)
            ?? throw new KeyNotFoundException("Funding event not found.");
        if (fundingEvent.Status != FundingEventStatuses.Requested)
            throw new ArgumentException($"Only a Requested event can be decided (current: {fundingEvent.Status}).");

        fundingEvent.Status = approve ? FundingEventStatuses.Approved : FundingEventStatuses.Rejected;
        fundingEvent.DecidedBy = decider.Identifier;
        fundingEvent.DecidedAt = DateTimeOffset.UtcNow;
        fundingEvent.DecisionComment = comment;
        await _events.UpdateAsync(fundingEvent, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = fundingEvent.Id,
            Category = AuditCategories.FundingEventDecided,
            Actor = decider.Identifier,
            Summary = $"{fundingEvent.EventType} of {fundingEvent.Amount:N0} {fundingEvent.Status.ToLowerInvariant()} by {decider.Identifier}."
                + (string.IsNullOrWhiteSpace(comment) ? "" : $" Comment: {comment}"),
            Detail = new Dictionary<string, string>
            {
                ["eventId"] = fundingEvent.Id.ToString(),
                ["status"] = fundingEvent.Status,
                ["amount"] = fundingEvent.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        }, ct);

        return fundingEvent;
    }

    public async Task<FundingEvent> MarkDisbursedAsync(
        string tenantId, Guid eventId, string actor, CancellationToken ct = default)
    {
        var fundingEvent = await _events.GetAsync(tenantId, eventId, ct)
            ?? throw new KeyNotFoundException("Funding event not found.");
        if (fundingEvent.Status != FundingEventStatuses.Approved)
            throw new ArgumentException($"Only an Approved event can be disbursed (current: {fundingEvent.Status}).");

        fundingEvent.Status = FundingEventStatuses.Disbursed;
        fundingEvent.DisbursedAt = DateTimeOffset.UtcNow;
        await _events.UpdateAsync(fundingEvent, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = fundingEvent.Id,
            Category = AuditCategories.FundingEventDisbursed,
            Actor = actor,
            Summary = $"{fundingEvent.EventType} of {fundingEvent.Amount:N0} disbursed/received.",
            Detail = new Dictionary<string, string>
            {
                ["eventId"] = fundingEvent.Id.ToString(),
                ["amount"] = fundingEvent.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        }, ct);

        return fundingEvent;
    }
}
