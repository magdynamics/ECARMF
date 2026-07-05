using ECARMF.Kernel.Application.Billing;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// The month-close billing cycle: every six hours, any active tenant
/// missing its previous-month statement gets one. Idempotent per tenant
/// per month, like the report cycle it mirrors.
/// </summary>
public class MonthlyBillingHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyBillingHostedService> _logger;

    public MonthlyBillingHostedService(
        IServiceScopeFactory scopeFactory, ILogger<MonthlyBillingHostedService> logger)
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
                var billing = scope.ServiceProvider.GetRequiredService<IMonthlyBillingService>();
                var generated = await billing.EnsureMonthlyStatementsAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (generated > 0)
                {
                    _logger.LogInformation("Monthly billing cycle generated {Count} statement(s).", generated);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Monthly billing cycle failed.");
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
