using ECARMF.Kernel.Application.Integrations;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// Runs scheduled pull-mode integration feeds: every minute, any Active pull
/// integration whose interval has elapsed is fetched. Failures are recorded
/// as feed runs (visible in integration health), never thrown away.
/// </summary>
public class FeedSchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeedSchedulerHostedService> _logger;

    public FeedSchedulerHostedService(IServiceScopeFactory scopeFactory, ILogger<FeedSchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationStore>();
                var feeds = scope.ServiceProvider.GetRequiredService<IIntegrationFeedService>();

                var due = await integrations.GetDueScheduledPullsAsync(DateTimeOffset.UtcNow, stoppingToken);
                foreach (var integration in due)
                {
                    var run = await feeds.PullAsync(
                        integration.TenantId, integration.IntegrationId,
                        "system:flywheel", "pull-scheduled", stoppingToken);
                    _logger.LogInformation(
                        "Scheduled pull for {Tenant}/{Integration}: {Status} ({Records} records).",
                        integration.TenantId, integration.IntegrationId,
                        run.Success ? "succeeded" : $"failed ({run.Error})", run.RecordsIngested);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feed scheduler pass failed.");
            }
        }
    }
}
