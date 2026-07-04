using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfOutcomeStore : IOutcomeStore
{
    private readonly ECARMFDbContext _db;

    public EfOutcomeStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(TransactionOutcome outcome, CancellationToken ct = default)
    {
        _db.TransactionOutcomes.Add(new OutcomeRecord
        {
            Id = outcome.Id,
            TenantId = outcome.TenantId,
            TransactionId = outcome.TransactionId,
            EventName = outcome.EventName,
            Outcome = outcome.Outcome.ToString(),
            Reason = outcome.Reason,
            RuleId = outcome.RuleId,
            PackageId = outcome.PackageId,
            PackageVersion = outcome.PackageVersion,
            ProcessedAt = outcome.ProcessedAt
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TransactionOutcome>> GetForTransactionsAsync(
        string tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default)
    {
        var records = await _db.TransactionOutcomes.AsNoTracking()
            .Where(o => o.TenantId == tenantId && transactionIds.Contains(o.TransactionId))
            .OrderBy(o => o.ProcessedAt)
            .ToListAsync(ct);

        return records.Select(r => new TransactionOutcome
        {
            Id = r.Id,
            TenantId = r.TenantId,
            TransactionId = r.TransactionId,
            EventName = r.EventName,
            Outcome = Enum.Parse<RuleOutcome>(r.Outcome),
            Reason = r.Reason,
            RuleId = r.RuleId,
            PackageId = r.PackageId,
            PackageVersion = r.PackageVersion,
            ProcessedAt = r.ProcessedAt
        }).ToList();
    }
}
