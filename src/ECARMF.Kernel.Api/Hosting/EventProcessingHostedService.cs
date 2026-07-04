using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Processing;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// Single consumer of the kernel event bus. Creates a DI scope per event and
/// delegates to the EventProcessor. A failure processing one event is logged
/// and never stops the stream.
/// </summary>
public class EventProcessingHostedService : BackgroundService
{
    private readonly IKernelEventBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventProcessingHostedService> _logger;

    public EventProcessingHostedService(
        IKernelEventBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<EventProcessingHostedService> logger)
    {
        _bus = bus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event processing engine started.");

        await foreach (var kernelEvent in _bus.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IEventProcessor>();
                var result = await processor.ProcessAsync(kernelEvent, stoppingToken);

                _logger.LogInformation(
                    "Processed event {EventName} for transaction {TransactionId}: {Outcome} ({RuleCount} rules evaluated).",
                    result.EventName,
                    result.CorrelationId,
                    result.Outcome?.Outcome.ToString() ?? "no outcome",
                    result.Evaluations.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed processing event {EventName} for transaction {TransactionId}.",
                    kernelEvent.EventName, kernelEvent.CorrelationId);
            }
        }
    }
}
