using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Relationships;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Relationships;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Tests.Fakes;

public class InMemoryPackageStore : IPackageStore
{
    private readonly List<StoredPackage> _packages = [];

    public IReadOnlyList<StoredPackage> Items => _packages;

    public Task<bool> ExistsAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default) =>
        Task.FromResult(_packages.Any(p => Matches(p, tenantId, packageId, packageVersion)));

    public Task AddAsync(string tenantId, KnowledgePackageManifest manifest, PackageLoadState state, string? statusDetail, CancellationToken ct = default)
    {
        _packages.Add(new StoredPackage(tenantId, manifest, state, statusDetail));
        return Task.CompletedTask;
    }

    public Task<StoredPackage?> GetAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default) =>
        Task.FromResult(_packages.FirstOrDefault(p => Matches(p, tenantId, packageId, packageVersion)));

    public Task UpdateStateAsync(string tenantId, string packageId, string packageVersion, PackageLoadState state, string? statusDetail, CancellationToken ct = default)
    {
        var index = _packages.FindIndex(p => Matches(p, tenantId, packageId, packageVersion));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' version '{packageVersion}' is not persisted for tenant '{tenantId}'.");
        }

        _packages[index] = _packages[index] with { State = state, StatusDetail = statusDetail };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredPackage>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredPackage>>(
            _packages.Where(p => SameTenant(p, tenantId)).ToList());

    public Task<IReadOnlyList<StoredPackage>> GetByStateAsync(string tenantId, PackageLoadState state, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredPackage>>(
            _packages.Where(p => SameTenant(p, tenantId) && p.State == state).ToList());

    public Task<IReadOnlyList<StoredPackage>> GetByStateAllTenantsAsync(PackageLoadState state, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredPackage>>(_packages.Where(p => p.State == state).ToList());

    private static bool SameTenant(StoredPackage p, string tenantId) =>
        string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(StoredPackage p, string tenantId, string packageId, string packageVersion) =>
        SameTenant(p, tenantId)
        && string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(p.Manifest.PackageVersion, packageVersion, StringComparison.OrdinalIgnoreCase);
}

public class InMemoryTransactionStore : ITransactionStore
{
    public List<Transaction> Items { get; } = [];

    public Task AppendAsync(Transaction transaction, CancellationToken ct = default)
    {
        Items.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Transaction>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Transaction>>(
            Items.Where(t => string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.ReceivedAt)
                .Take(limit)
                .ToList());

    public Task<Transaction?> GetByIdAsync(string tenantId, Guid transactionId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t =>
            string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && t.TransactionId == transactionId));

    public Task<(IReadOnlyList<Transaction> Items, int Total)> QueryAsync(
        TransactionQuery query, CancellationToken ct = default)
    {
        // Outcome filtering requires the outcome store and is exercised via
        // the EF implementation; the in-memory fake covers the other filters.
        var filtered = Items
            .Where(t => string.Equals(t.TenantId, query.TenantId, StringComparison.OrdinalIgnoreCase))
            .Where(t => query.RecordType is null || string.Equals(t.TransactionType, query.RecordType, StringComparison.OrdinalIgnoreCase))
            .Where(t => query.SubmittedBy is null || string.Equals(t.SubmittedBy, query.SubmittedBy, StringComparison.OrdinalIgnoreCase))
            .Where(t => query.From is null || t.ReceivedAt >= query.From)
            .Where(t => query.To is null || t.ReceivedAt <= query.To)
            .Where(t => string.IsNullOrWhiteSpace(query.Text)
                || t.TransactionType.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
                || t.SubmittedBy.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
                || t.Payload.Any(kv => kv.Value.Contains(query.Text, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.ReceivedAt)
            .ToList();

        return Task.FromResult<(IReadOnlyList<Transaction>, int)>(
            (filtered.Skip(query.Skip).Take(query.Take).ToList(), filtered.Count));
    }

    public Task<IReadOnlyList<string>> GetRecordTypesAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Items.Where(t => string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TransactionType).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t).ToList());
}

public class InMemoryScoreStore : IScoreStore
{
    public List<ScoreRecord> Items { get; } = [];

    public Task AppendAsync(ScoreRecord score, CancellationToken ct = default)
    {
        Items.Add(score);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoreRecord>> GetHistoryAsync(
        string tenantId, string subjectType, string subjectId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScoreRecord>>(
            Items.Where(s => string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(s.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(s.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.ComputedAt).ToList());

    public Task<IReadOnlyList<ScoreRecord>> GetRecentAsync(
        string tenantId, int limit, string? scoreType = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScoreRecord>>(
            Items.Where(s => string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && (scoreType is null || string.Equals(s.ScoreType, scoreType, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(s => s.ComputedAt).Take(limit).ToList());

    public Task<IReadOnlyList<ScoreRecord>> GetRecentByTypeAllTenantsAsync(
        string scoreType, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScoreRecord>>(
            Items.Where(s => string.Equals(s.ScoreType, scoreType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.ComputedAt).Take(limit).ToList());
}

public class InMemoryApprovalStore : IApprovalStore
{
    public List<ApprovalDecision> Items { get; } = [];

    public Task AppendAsync(ApprovalDecision decision, CancellationToken ct = default)
    {
        Items.Add(decision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApprovalDecision>> GetForTransactionAsync(
        string tenantId, Guid transactionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApprovalDecision>>(
            Items.Where(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && a.TransactionId == transactionId)
                .OrderBy(a => a.DecidedAt)
                .ToList());
}

public class InMemoryOutcomeStore : IOutcomeStore
{
    public List<TransactionOutcome> Items { get; } = [];

    public Task AppendAsync(TransactionOutcome outcome, CancellationToken ct = default)
    {
        Items.Add(outcome);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TransactionOutcome>> GetForTransactionsAsync(
        string tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TransactionOutcome>>(
            Items.Where(o => string.Equals(o.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && transactionIds.Contains(o.TransactionId))
                .OrderBy(o => o.ProcessedAt)
                .ToList());
}

public class InMemoryAuditLog : IAuditLog
{
    public List<AuditEntry> Items { get; } = [];

    public Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        Items.Add(entry);
        return Task.CompletedTask;
    }

    public Task AppendManyAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        Items.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByCorrelationAsync(string tenantId, Guid correlationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(
            Items.Where(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && a.CorrelationId == correlationId)
                .OrderBy(a => a.OccurredAt).ToList());

    public Task<IReadOnlyList<AuditEntry>> GetByTimeRangeAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(
            Items.Where(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && a.OccurredAt >= from && a.OccurredAt <= to)
                .OrderBy(a => a.OccurredAt).ToList());
}

public class InMemoryEntityRelationshipStore : IEntityRelationshipStore
{
    public List<EntityRelationship> Items { get; } = [];

    public Task<EntityRelationship?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(r =>
            string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) && r.Id == id));

    public Task<IReadOnlyList<EntityRelationship>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EntityRelationship>>(
            Items.Where(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<EntityRelationship>> GetBySubjectAsync(
        string tenantId, string subjectType, string subjectId,
        string? relationshipType = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EntityRelationship>>(
            Items.Where(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(r.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(r.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)
                          && (relationshipType is null
                              || string.Equals(r.RelationshipType, relationshipType, StringComparison.OrdinalIgnoreCase)))
                .ToList());

    public Task AddAsync(EntityRelationship relationship, CancellationToken ct = default)
    {
        Items.Add(relationship);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(EntityRelationship relationship, CancellationToken ct = default)
    {
        Items.RemoveAll(r => r.Id == relationship.Id);
        Items.Add(relationship);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.RemoveAll(r =>
            string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) && r.Id == id) > 0);
}
