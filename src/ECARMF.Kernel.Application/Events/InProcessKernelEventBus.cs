using System.Threading.Channels;

namespace ECARMF.Kernel.Application.Events;

/// <summary>Channel-backed single-process bus. Unbounded: intake must never
/// block on downstream processing (the transaction is already persisted).</summary>
public class InProcessKernelEventBus : IKernelEventBus
{
    private readonly Channel<KernelEvent> _channel = Channel.CreateUnbounded<KernelEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask PublishAsync(KernelEvent kernelEvent, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(kernelEvent, ct);

    public IAsyncEnumerable<KernelEvent> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);
}
