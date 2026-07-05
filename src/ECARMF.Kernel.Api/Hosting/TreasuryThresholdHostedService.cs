using ECARMF.Kernel.Application.Treasury;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// The rolling AI treasury function: every six hours, every enabled sweep
/// account's threshold is re-derived from its trailing balance history.
/// Proposals surface for human review (Recommend-Only) — this cycle never
/// changes a standing threshold by itself.
/// </summary>
public class TreasuryThresholdHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TreasuryThresholdHostedService> _logger;

    public TreasuryThresholdHostedService(
        IServiceScopeFactory scopeFactory, ILogger<TreasuryThresholdHostedService> logger)
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
                var treasury = scope.ServiceProvider.GetRequiredService<ITreasurySweepService>();
                var proposals = await treasury.RecalculateThresholdsAsync(null, DateTimeOffset.UtcNow, stoppingToken);
                if (proposals > 0)
                {
                    _logger.LogInformation("AI treasury pass raised {Count} threshold proposal(s).", proposals);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI treasury pass failed.");
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
