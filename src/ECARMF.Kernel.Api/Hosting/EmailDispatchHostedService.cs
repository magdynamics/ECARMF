using ECARMF.Kernel.Application.Notifications;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// Carries alarms to inboxes: every minute, notifications not yet handled
/// are emailed per the platform mail settings. Idle (and free) until the
/// operator configures and enables mail delivery.
/// </summary>
public class EmailDispatchHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchHostedService> _logger;

    public EmailDispatchHostedService(
        IServiceScopeFactory scopeFactory, ILogger<EmailDispatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested && await WaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationEmailService>();
                var sent = await dispatcher.ProcessPendingAsync(50, stoppingToken);
                if (sent > 0)
                {
                    _logger.LogInformation("Email dispatch sent {Count} notification email(s).", sent);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dispatch pass failed.");
            }
        }
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
