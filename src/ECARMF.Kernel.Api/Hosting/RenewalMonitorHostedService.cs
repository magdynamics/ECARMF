using ECARMF.Kernel.Application.Compliance;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// The calendar check: once shortly after startup and then every hour, every
/// Active renewal commitment across all tenants is evaluated against its
/// warning ladder. Alerting is idempotent per ladder rung, so the hourly
/// cadence costs nothing when no due date has come closer.
/// </summary>
public class RenewalMonitorHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RenewalMonitorHostedService> _logger;

    public RenewalMonitorHostedService(
        IServiceScopeFactory scopeFactory, ILogger<RenewalMonitorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var monitor = scope.ServiceProvider.GetRequiredService<IRenewalMonitor>();
                var raised = await monitor.EvaluateAsync(null, DateTimeOffset.UtcNow, stoppingToken);
                if (raised > 0)
                {
                    _logger.LogInformation("Renewal monitor raised {Count} alert(s).", raised);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Renewal monitor pass failed.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
