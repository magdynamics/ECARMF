using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>
/// Persistence port for transactions. Writes are append-only: no update or
/// delete methods exist by design, so immutability of received transactions
/// is structural. Reads are tenant-scoped.
/// </summary>
public interface ITransactionStore
{
    Task AppendAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>Most recent transactions for a tenant, newest first.</summary>
    Task<IReadOnlyList<Transaction>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);

    Task<Transaction?> GetByIdAsync(string tenantId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Server-side metadata query with paging: built for tenants with
    /// thousands of records. Text search covers type, submitter, and payload.</summary>
    Task<(IReadOnlyList<Transaction> Items, int Total)> QueryAsync(TransactionQuery query, CancellationToken ct = default);

    /// <summary>Distinct record types the tenant has received — drives filters.</summary>
    Task<IReadOnlyList<string>> GetRecordTypesAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>Metadata filters for record activity. Outcome filtering joins the
/// outcome store; null filters are skipped.</summary>
public sealed record TransactionQuery(
    string TenantId,
    string? RecordType = null,
    string? Outcome = null,
    string? SubmittedBy = null,
    string? Text = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Skip = 0,
    int Take = 50,
    string? CaseId = null);
