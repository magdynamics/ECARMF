using ECARMF.Kernel.Domain.Entities;

namespace ECARMF.Kernel.Domain.Transactions;

/// <summary>
/// A transaction as received by the kernel. Deliberately generic: the kernel
/// does not know what a transaction means — TransactionType and the payload
/// fields get their meaning from Knowledge Package declarations. The record
/// is immutable once persisted; outcomes are recorded separately so the
/// received facts are never rewritten.
/// </summary>
public class Transaction : UniversalBaseEntity
{
    /// <summary>The transaction's identity is its entity id.</summary>
    public Guid TransactionId => EntityId;

    /// <summary>Package-defined type discriminator (e.g. "withdrawal").</summary>
    public string TransactionType { get; set; } = string.Empty;

    public string SubmittedBy { get; set; } = string.Empty;

    /// <summary>Field values rules evaluate against, as declared by packages.</summary>
    public Dictionary<string, string> Payload { get; set; } = [];

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
