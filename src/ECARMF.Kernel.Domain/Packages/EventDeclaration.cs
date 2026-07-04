namespace ECARMF.Kernel.Domain.Packages;

/// <summary>Declares an event type contributed by a Knowledge Package
/// (e.g. TransactionReceived, TransactionApproved).</summary>
public class EventDeclaration
{
    public string EventName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<AttributeDeclaration> PayloadFields { get; set; } = [];
}
