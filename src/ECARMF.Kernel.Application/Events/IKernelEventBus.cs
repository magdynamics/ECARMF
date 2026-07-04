namespace ECARMF.Kernel.Application.Events;

/// <summary>In-process event bus. Publishers enqueue; the event processing
/// engine is the single consumer.</summary>
public interface IKernelEventBus
{
    ValueTask PublishAsync(KernelEvent kernelEvent, CancellationToken ct = default);

    /// <summary>Continuous stream of published events, in publish order.</summary>
    IAsyncEnumerable<KernelEvent> ReadAllAsync(CancellationToken ct = default);
}
