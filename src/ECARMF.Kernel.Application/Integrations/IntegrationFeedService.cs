using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Integrations;

namespace ECARMF.Kernel.Application.Integrations;

/// <summary>Tenant-scoped integration configuration + feed run history.</summary>
public interface IIntegrationStore
{
    Task<IntegrationDefinition?> GetAsync(string tenantId, string integrationId, CancellationToken ct = default);
    Task<IReadOnlyList<IntegrationDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(IntegrationDefinition integration, CancellationToken ct = default);
    Task UpdateAsync(IntegrationDefinition integration, CancellationToken ct = default);
    Task AddRunAsync(FeedRun run, CancellationToken ct = default);
    Task<IReadOnlyList<FeedRun>> GetRunsAsync(string tenantId, string? integrationId, int limit, CancellationToken ct = default);

    /// <summary>Pull-mode integrations across ALL tenants that are due for a
    /// scheduled fetch (Active, interval set, last feed older than interval).</summary>
    Task<IReadOnlyList<IntegrationDefinition>> GetDueScheduledPullsAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Stores/replaces the integration's protected auth secret (pull mode).</summary>
    Task SetAuthSecretAsync(string tenantId, string integrationId, string? secret, CancellationToken ct = default);

    Task<string?> GetAuthSecretAsync(string tenantId, string integrationId, CancellationToken ct = default);
}

/// <summary>Fetches a pull-mode feed from the external application's export
/// endpoint. Implemented in Infrastructure (HTTP); a smarter transport
/// (OAuth token exchange, SDK client) never changes this contract.</summary>
public interface IFeedPuller
{
    Task<string> FetchAsync(string url, string? bearerSecret, CancellationToken ct = default);
}

public interface IIntegrationFeedService
{
    /// <summary>The application pushed a feed to the platform.</summary>
    Task<FeedRun> PushAsync(string tenantId, string integrationId, string rawPayload, string actor, CancellationToken ct = default);

    /// <summary>The platform fetches from the application's export endpoint.</summary>
    Task<FeedRun> PullAsync(string tenantId, string integrationId, string actor, string trigger = "pull-manual", CancellationToken ct = default);
}

/// <summary>
/// The one feed mechanism behind every managed integration: resolve the
/// integration, obtain the payload (pushed or pulled), run it through the
/// integration's connector — same mapping, provenance stamping, library
/// archiving, and intake as every other source — and record the run, success
/// or failure, in the integration's health history.
/// </summary>
public class IntegrationFeedService : IIntegrationFeedService
{
    private readonly IIntegrationStore _integrations;
    private readonly IDataSourceConnector _ingestion;
    private readonly IFeedPuller _puller;
    private readonly IAuditLog _audit;

    public IntegrationFeedService(
        IIntegrationStore integrations,
        IDataSourceConnector ingestion,
        IFeedPuller puller,
        IAuditLog audit)
    {
        _integrations = integrations;
        _ingestion = ingestion;
        _puller = puller;
        _audit = audit;
    }

    public async Task<FeedRun> PushAsync(
        string tenantId, string integrationId, string rawPayload, string actor, CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(tenantId, integrationId, ct);
        return integration is null
            ? await RecordFailureAsync(tenantId, integrationId, "push", actor,
                $"Integration '{integrationId}' is not configured for this tenant.", ct)
            : await ExecuteAsync(integration, rawPayload, "push", actor, ct);
    }

    public async Task<FeedRun> PullAsync(
        string tenantId, string integrationId, string actor, string trigger = "pull-manual", CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(tenantId, integrationId, ct);
        if (integration is null)
        {
            return await RecordFailureAsync(tenantId, integrationId, trigger, actor,
                $"Integration '{integrationId}' is not configured for this tenant.", ct);
        }

        if (string.IsNullOrWhiteSpace(integration.PullUrl))
        {
            return await RecordFailureAsync(tenantId, integrationId, trigger, actor,
                "Integration has no pull URL configured.", ct);
        }

        string payload;
        try
        {
            var secret = await _integrations.GetAuthSecretAsync(tenantId, integrationId, ct);
            payload = await _puller.FetchAsync(integration.PullUrl, secret, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = await RecordFailureAsync(tenantId, integrationId, trigger, actor,
                $"Fetch from '{integration.PullUrl}' failed: {ex.Message}", ct);
            await UpdateHealthAsync(integration, failure, ct);
            return failure;
        }

        var run = await ExecuteAsync(integration, payload, trigger, actor, ct);
        return run;
    }

    private async Task<FeedRun> ExecuteAsync(
        IntegrationDefinition integration, string rawPayload, string trigger, string actor, CancellationToken ct)
    {
        var run = new FeedRun
        {
            TenantId = integration.TenantId,
            IntegrationId = integration.IntegrationId,
            Trigger = trigger,
            TriggeredBy = actor
        };

        if (!string.Equals(integration.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            run.Success = false;
            run.Error = $"Integration '{integration.IntegrationId}' is {integration.Status}.";
        }
        else
        {
            var result = await _ingestion.IngestAsync(
                integration.TenantId, integration.ConnectorId, rawPayload, actor, ct);
            run.Success = result.Success;
            run.RecordsIngested = result.RecordIds.Count;
            run.Error = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : null;
        }

        run.FinishedAt = DateTimeOffset.UtcNow;
        await _integrations.AddRunAsync(run, ct);
        await UpdateHealthAsync(integration, run, ct);
        await AuditRunAsync(run, integration.ApplicationType, ct);
        return run;
    }

    private async Task<FeedRun> RecordFailureAsync(
        string tenantId, string integrationId, string trigger, string actor, string error, CancellationToken ct)
    {
        var run = new FeedRun
        {
            TenantId = tenantId,
            IntegrationId = integrationId,
            Trigger = trigger,
            TriggeredBy = actor,
            Success = false,
            Error = error,
            FinishedAt = DateTimeOffset.UtcNow
        };
        await _integrations.AddRunAsync(run, ct);
        await AuditRunAsync(run, "unknown", ct);
        return run;
    }

    private async Task UpdateHealthAsync(IntegrationDefinition integration, FeedRun run, CancellationToken ct)
    {
        integration.LastFeedAt = run.FinishedAt ?? DateTimeOffset.UtcNow;
        integration.LastFeedStatus = run.Success ? "Succeeded" : "Failed";
        await _integrations.UpdateAsync(integration, ct);
    }

    private Task AuditRunAsync(FeedRun run, string applicationType, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEntry
        {
            TenantId = run.TenantId,
            CorrelationId = run.Id,
            Category = AuditCategories.IntegrationFeedRun,
            Actor = run.TriggeredBy,
            Summary = run.Success
                ? $"Integration '{run.IntegrationId}' ({applicationType}) {run.Trigger} feed ingested {run.RecordsIngested} record(s)."
                : $"Integration '{run.IntegrationId}' ({applicationType}) {run.Trigger} feed failed: {run.Error}",
            Detail = new Dictionary<string, string>
            {
                ["integrationId"] = run.IntegrationId,
                ["trigger"] = run.Trigger,
                ["success"] = run.Success.ToString(),
                ["recordsIngested"] = run.RecordsIngested.ToString(),
                ["error"] = run.Error ?? string.Empty
            }
        }, ct);
}
