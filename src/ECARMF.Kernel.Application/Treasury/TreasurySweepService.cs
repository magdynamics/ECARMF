using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Treasury;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Treasury;

/// <summary>Tenant-scoped sweep account configuration store.</summary>
public interface ISweepAccountStore
{
    Task<SweepAccount?> GetAsync(string tenantId, string accountId, CancellationToken ct = default);
    Task<IReadOnlyList<SweepAccount>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<SweepAccount>> GetEnabledAllTenantsAsync(CancellationToken ct = default);
    Task AddAsync(SweepAccount account, CancellationToken ct = default);
    Task UpdateAsync(SweepAccount account, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string accountId, CancellationToken ct = default);
}

public sealed record SweepObservationResult(
    decimal Balance,
    bool SweepExecuted,
    decimal? SweepAmount,
    Guid? RecommendationId,
    bool PayrollAlertRaised);

public interface ITreasurySweepService
{
    /// <summary>The rolling AI treasury pass: re-derives every enabled
    /// account's threshold from its trailing balance history and, when the
    /// proposal differs meaningfully from the standing approved threshold,
    /// surfaces it for review (Recommend-Only) — every proposal is a
    /// versioned TreasuryThreshold ScoreRecord, never a silent overwrite.
    /// Returns the number of proposals raised.</summary>
    Task<int> RecalculateThresholdsAsync(string? tenantId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>A human accepts (or overrides) the proposed threshold —
    /// only this makes it the standing threshold sweeps execute against.</summary>
    Task<SweepAccount> ApproveThresholdAsync(
        string tenantId, string accountId, string approvedBy, decimal? overrideValue,
        CancellationToken ct = default);

    /// <summary>A balance observation (from a bank feed or manual entry).
    /// Operating account above its APPROVED threshold → the overage becomes
    /// an Autonomous-tier CapitalFlow (sweep to destination),
    /// fully reasoned and audited. Payroll account above threshold → a
    /// Recommend-Only alert, never a sweep.</summary>
    Task<SweepObservationResult> ObserveBalanceAsync(
        string tenantId, string accountId, decimal balance, string observedBy,
        CancellationToken ct = default);
}

/// <summary>
/// The AI Treasury function (Universal Dental Requirement 8). Thresholds
/// are continuously managed, not set once and frozen; threshold-setting is
/// Recommend-Only while sweep execution against a standing approved
/// threshold is Autonomous. The AI never approves its own proposal.
/// </summary>
public class TreasurySweepService : ITreasurySweepService
{
    /// <summary>Proposal = trailing mean balance × this buffer ratio: the
    /// account keeps roughly half its typical balance as working buffer,
    /// the rest is sweepable. Deliberately simple and explainable.</summary>
    public const decimal BufferRatio = 0.5m;

    /// <summary>Below this many balance observations, no proposal — the AI
    /// does not guess thresholds from insufficient history.</summary>
    public const int MinObservations = 3;

    /// <summary>Proposals within this relative distance of the standing
    /// approved threshold are suppressed — churn is not insight.</summary>
    public const decimal ProposalDeadband = 0.05m;

    public const string BalanceScoreType = "AccountBalance";
    public const string ThresholdScoreType = "TreasuryThreshold";
    private const string SubjectType = "treasury-account";

    private readonly ISweepAccountStore _accounts;
    private readonly IScoreStore _scores;
    private readonly ICapitalFlowStore _allocations;
    private readonly INotificationStore _notifications;
    private readonly IAuditLog _audit;

    public TreasurySweepService(
        ISweepAccountStore accounts, IScoreStore scores, ICapitalFlowStore allocations,
        INotificationStore notifications, IAuditLog audit)
    {
        _accounts = accounts;
        _scores = scores;
        _allocations = allocations;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<int> RecalculateThresholdsAsync(
        string? tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var accounts = tenantId is null
            ? await _accounts.GetEnabledAllTenantsAsync(ct)
            : (await _accounts.GetAllAsync(tenantId, ct)).Where(a => a.Enabled).ToList();

        var proposals = 0;
        foreach (var account in accounts)
        {
            var history = (await _scores.GetHistoryAsync(account.TenantId, SubjectType, account.AccountId, ct))
                .Where(s => s.ScoreType == BalanceScoreType)
                .OrderByDescending(s => s.ComputedAt)
                .Take(30)
                .Select(s => s.Value)
                .ToList();
            if (history.Count < MinObservations)
            {
                continue;
            }

            var proposal = Math.Round(history.Average() * BufferRatio / 100m, 0) * 100m;
            if (proposal <= 0)
            {
                continue;
            }

            // Deadband against the standing threshold AND the open proposal:
            // re-proposing the same number every pass is noise.
            if (WithinDeadband(proposal, account.ApprovedThreshold)
                || WithinDeadband(proposal, account.ProposedThreshold))
            {
                continue;
            }

            var reasoning =
                $"Trailing {history.Count} balance observation(s) average " +
                $"{Math.Round(history.Average(), 2).ToString(CultureInfo.InvariantCulture)}; " +
                $"buffer ratio {BufferRatio.ToString(CultureInfo.InvariantCulture)} keeps roughly half the typical " +
                $"balance as working cash. Proposed threshold {proposal.ToString(CultureInfo.InvariantCulture)}" +
                (account.ApprovedThreshold is { } current
                    ? $" (standing approved threshold {current.ToString(CultureInfo.InvariantCulture)})."
                    : " (no approved threshold yet — sweeps stay off until one is approved).");

            // The versioned trail: every recalculation is its own score,
            // never an overwrite of the prior one.
            await _scores.AppendAsync(new ScoreRecord
            {
                TenantId = account.TenantId,
                SubjectType = SubjectType,
                SubjectId = account.AccountId,
                ScoreType = ThresholdScoreType,
                Value = proposal,
                Provenance = "AIGenerated",
                CorrelationId = Guid.NewGuid(),
                Metadata = new Dictionary<string, string>
                {
                    ["methodology"] = $"trailing-mean x {BufferRatio}",
                    ["observations"] = history.Count.ToString(),
                    ["standingThreshold"] = account.ApprovedThreshold?.ToString(CultureInfo.InvariantCulture) ?? ""
                }
            }, ct);

            account.ProposedThreshold = proposal;
            account.ProposedAt = now;
            account.ProposalReasoning = reasoning;
            account.UpdatedAt = now;
            await _accounts.UpdateAsync(account, ct);

            await _notifications.AddAsync(new NotificationItem
            {
                TenantId = account.TenantId,
                WorkflowId = $"treasury:{account.AccountId}",
                Target = "TreasuryOfficer",
                Message = $"AI Treasury proposes threshold {proposal:N0} for '{account.Name}' — review and approve " +
                          $"before it takes effect. {reasoning}",
                Severity = "Info",
                CorrelationId = Guid.NewGuid()
            }, ct);

            await _audit.AppendAsync(new AuditEntry
            {
                TenantId = account.TenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.TreasuryThresholdProposed,
                Actor = "system:flywheel",
                Summary = $"Threshold {proposal:N0} proposed for account '{account.AccountId}' (Recommend-Only).",
                Detail = new Dictionary<string, string>
                {
                    ["accountId"] = account.AccountId,
                    ["proposal"] = proposal.ToString(CultureInfo.InvariantCulture),
                    ["standing"] = account.ApprovedThreshold?.ToString(CultureInfo.InvariantCulture) ?? "",
                    ["reasoning"] = reasoning
                }
            }, ct);
            proposals++;
        }

        return proposals;
    }

    public async Task<SweepAccount> ApproveThresholdAsync(
        string tenantId, string accountId, string approvedBy, decimal? overrideValue,
        CancellationToken ct = default)
    {
        var account = await _accounts.GetAsync(tenantId, accountId, ct)
            ?? throw new KeyNotFoundException($"Sweep account '{accountId}' does not exist.");

        var value = overrideValue ?? account.ProposedThreshold
            ?? throw new ArgumentException("Nothing to approve: no proposed threshold and no override value.");
        if (value <= 0)
        {
            throw new ArgumentException("The threshold must be positive.");
        }

        var previous = account.ApprovedThreshold;
        account.ApprovedThreshold = value;
        account.ApprovedBy = approvedBy;
        account.ApprovedAt = DateTimeOffset.UtcNow;
        account.ProposedThreshold = null;
        account.ProposedAt = null;
        account.ProposalReasoning = null;
        account.UpdatedAt = account.ApprovedAt;
        await _accounts.UpdateAsync(account, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.TreasuryThresholdApproved,
            Actor = approvedBy,
            Summary = $"Threshold for '{account.AccountId}' approved at {value:N0}"
                + (overrideValue is not null ? " (human override of the AI proposal)." : " (AI proposal accepted).")
                + (previous is { } p ? $" Previous standing threshold: {p:N0}." : ""),
            Detail = new Dictionary<string, string>
            {
                ["accountId"] = account.AccountId,
                ["approved"] = value.ToString(CultureInfo.InvariantCulture),
                ["previous"] = previous?.ToString(CultureInfo.InvariantCulture) ?? "",
                ["overridden"] = (overrideValue is not null).ToString()
            }
        }, ct);

        return account;
    }

    public async Task<SweepObservationResult> ObserveBalanceAsync(
        string tenantId, string accountId, decimal balance, string observedBy,
        CancellationToken ct = default)
    {
        var account = await _accounts.GetAsync(tenantId, accountId, ct)
            ?? throw new KeyNotFoundException($"Sweep account '{accountId}' does not exist.");

        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();

        var balanceScore = new ScoreRecord
        {
            TenantId = tenantId,
            SubjectType = SubjectType,
            SubjectId = accountId,
            ScoreType = BalanceScoreType,
            Value = balance,
            Provenance = "ExternalSystemVerified",
            CorrelationId = correlationId
        };
        await _scores.AppendAsync(balanceScore, ct);

        account.LastObservedBalance = balance;
        account.LastObservedAt = now;

        if (!account.Enabled || account.ApprovedThreshold is not { } threshold || balance <= threshold)
        {
            account.UpdatedAt = now;
            await _accounts.UpdateAsync(account, ct);
            return new SweepObservationResult(balance, false, null, null, false);
        }

        if (account.Kind == SweepAccountKinds.Payroll)
        {
            // Never sweep payroll: pay-cycle balances are naturally lumpy.
            // An abnormally high balance is a question for a human.
            await _notifications.AddAsync(new NotificationItem
            {
                TenantId = tenantId,
                WorkflowId = $"treasury:{accountId}",
                Target = "TreasuryOfficer",
                Message = $"Payroll account '{account.Name}' balance {balance:N0} exceeds its watch level " +
                          $"{threshold:N0} — review whether cash is idle after the pay run. No sweep was executed.",
                Severity = "Warning",
                CorrelationId = correlationId
            }, ct);
            await _audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                Category = AuditCategories.PayrollBalanceFlagged,
                Actor = "system:flywheel",
                Summary = $"Payroll account '{accountId}' flagged high ({balance:N0} > {threshold:N0}); sweep intentionally NOT executed.",
                Detail = new Dictionary<string, string>
                {
                    ["accountId"] = accountId,
                    ["balance"] = balance.ToString(CultureInfo.InvariantCulture),
                    ["watchLevel"] = threshold.ToString(CultureInfo.InvariantCulture)
                }
            }, ct);
            account.UpdatedAt = now;
            await _accounts.UpdateAsync(account, ct);
            return new SweepObservationResult(balance, false, null, null, true);
        }

        // Operating account over its standing approved threshold: the
        // overage sweeps autonomously — same institution, same tenant,
        // standing human-approved threshold. Every sweep fully reasoned.
        var overage = balance - threshold;
        var latestThresholdScore = (await _scores.GetHistoryAsync(tenantId, SubjectType, accountId, ct))
            .Where(s => s.ScoreType == ThresholdScoreType)
            .OrderByDescending(s => s.ComputedAt)
            .FirstOrDefault();

        var recommendation = new CapitalFlow
        {
            TenantId = tenantId,
            Direction = CapitalFlowDirections.Outbound,
            SourceId = account.AccountId,
            TargetReference = account.DestinationAccountId ?? "corporate-operating",
            TargetAssetClass = "Cash",
            Amount = overage,
            TargetInstitution = account.Institution,
            ConfidenceScore = 0.9m,
            Reasoning =
                $"Treasury sweep: '{account.Name}' observed balance {balance:N0} exceeds the standing approved " +
                $"threshold {threshold:N0} (approved by {account.ApprovedBy} on {account.ApprovedAt:yyyy-MM-dd}). " +
                $"Overage {overage:N0} sweeps to '{account.DestinationAccountId ?? "corporate-operating"}' — " +
                "same institution, same tenant, standing threshold: Autonomous tier.",
            Assumptions =
            [
                "Same-institution transfer settles same-day with no settlement risk.",
                "The approved threshold already reserves the account's working buffer."
            ],
            RiskFactors = ["A pending large debit could arrive after this observation."],
            SupportingScoreRecordIds = latestThresholdScore is null
                ? [balanceScore.Id]
                : [balanceScore.Id, latestThresholdScore.Id],
            Tier = AutonomyTier.Autonomous,
            Status = "AutoExecuted",
            CorrelationId = correlationId
        };
        await _allocations.AddAsync(recommendation, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = AuditCategories.TreasurySweepExecuted,
            Actor = "system:flywheel",
            Summary = $"Autonomous sweep: {overage:N0} from '{accountId}' to " +
                      $"'{account.DestinationAccountId ?? "corporate-operating"}' (balance {balance:N0} > threshold {threshold:N0}).",
            Detail = new Dictionary<string, string>
            {
                ["accountId"] = accountId,
                ["balance"] = balance.ToString(CultureInfo.InvariantCulture),
                ["thresholdInEffect"] = threshold.ToString(CultureInfo.InvariantCulture),
                ["overage"] = overage.ToString(CultureInfo.InvariantCulture),
                ["destination"] = account.DestinationAccountId ?? "corporate-operating",
                ["recommendationId"] = recommendation.Id.ToString()
            }
        }, ct);

        account.LastSweepAt = now;
        account.UpdatedAt = now;
        await _accounts.UpdateAsync(account, ct);
        return new SweepObservationResult(balance, true, overage, recommendation.Id, false);
    }

    private static bool WithinDeadband(decimal proposal, decimal? reference) =>
        reference is { } r && r != 0 && Math.Abs(proposal - r) / r < ProposalDeadband;
}
