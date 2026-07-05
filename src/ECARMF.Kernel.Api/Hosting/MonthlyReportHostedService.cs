using ECARMF.Kernel.Application.Reporting;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// The monthly report cycle: every six hours the service checks whether any
/// Active tenant is missing its previous-month report and generates (and
/// emails) the gaps. Idempotent per tenant per month, so the cadence only
/// determines how soon after month-end the reports land.
/// </summary>
public class MonthlyReportHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyReportHostedService> _logger;

    public MonthlyReportHostedService(
        IServiceScopeFactory scopeFactory, ILogger<MonthlyReportHostedService> logger)
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
                var reports = scope.ServiceProvider.GetRequiredService<IClientReportService>();
                var generated = await reports.EnsureMonthlyReportsAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (generated > 0)
                {
                    _logger.LogInformation("Monthly report cycle generated {Count} report(s).", generated);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Monthly report cycle failed.");
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
