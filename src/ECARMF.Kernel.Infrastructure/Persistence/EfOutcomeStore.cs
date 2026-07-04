using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Transactions;

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
}
