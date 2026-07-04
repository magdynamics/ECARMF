namespace ECARMF.Kernel.Application.Events;

/// <summary>
/// Kernel-level event names. Transaction intake is kernel mechanism, so the
/// intake event name is a kernel constant — but the event only flows once an
/// active Knowledge Package declares it, keeping behavior package-driven.
/// </summary>
public static class KernelEventNames
{
    public const string TransactionReceived = "TransactionReceived";
}
