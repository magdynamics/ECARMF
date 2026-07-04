using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfApprovalStore : IApprovalStore
{
    private readonly ECARMFDbContext _db;

    public EfApprovalStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(ApprovalDecision decision, CancellationToken ct = default)
    {
        _db.Approvals.Add(new ApprovalRecord
        {
            Id = decision.Id,
            TenantId = decision.TenantId,
            TransactionId = decision.TransactionId,
            Approver = decision.Approver,
            Verdict = decision.Verdict.ToString(),
            Comment = decision.Comment,
            DecidedAt = decision.DecidedAt
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApprovalDecision>> GetForTransactionAsync(
        string tenantId, Guid transactionId, CancellationToken ct = default)
    {
        var records = await _db.Approvals.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.TransactionId == transactionId)
            .OrderBy(a => a.DecidedAt)
            .ToListAsync(ct);

        return records.Select(r => new ApprovalDecision
        {
            Id = r.Id,
            TenantId = r.TenantId,
            TransactionId = r.TransactionId,
            Approver = r.Approver,
            Verdict = Enum.Parse<ApprovalVerdict>(r.Verdict),
            Comment = r.Comment,
            DecidedAt = r.DecidedAt
        }).ToList();
    }
}
