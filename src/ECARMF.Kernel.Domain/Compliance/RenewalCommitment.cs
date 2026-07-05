namespace ECARMF.Kernel.Domain.Compliance;

/// <summary>
/// A dated obligation that lapses if nobody acts: a professional license, an
/// insurance policy, a loan installment, a business license, a corporate
/// registration, a lease. Failure-to-renew is one of the most avoidable
/// risks a business carries — the kernel watches the calendar so no due date
/// arrives unannounced. Like benchmarks, renewals are runtime configuration
/// (deliberately not package-versioned): the tenant states the commitment
/// and its warning ladder, the monitor raises escalating alerts as the due
/// date approaches and a Critical alarm once it passes.
/// </summary>
public class RenewalCommitment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>What lapses, e.g. "CPA license — Jane", "GL insurance policy".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Open renewal type (Batch 1, Refinement 1): License,
    /// Insurance, AnnualReport, CPE, COI, LeaseContract — or any future
    /// type. Never a closed enum; RenewalCategories.All is UI suggestions.</summary>
    public string Category { get; set; } = RenewalCategories.License;

    /// <summary>What the obligation belongs to (polymorphic): an
    /// OrganizationalUnit (a practice's license), a User (a CPA's CPE),
    /// a Subcontractor (a COI) — or any future subject type.</summary>
    public string? SubjectType { get; set; }

    public string? SubjectId { get; set; }

    /// <summary>Who the obligation is with (state board, insurer, lender...).</summary>
    public string? Counterparty { get; set; }

    /// <summary>Policy / license / account number.</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset DueDate { get; set; }

    /// <summary>Months between renewals; null means a one-time obligation
    /// that completes when marked renewed.</summary>
    public int? RecurrenceMonths { get; set; }

    /// <summary>Warning ladder in days before the due date, descending
    /// (e.g. 90, 30, 7): the earliest rung alerts as Info, the middle rungs
    /// as Warning, the last rung — and overdue — as Critical.</summary>
    public IReadOnlyList<int> LeadTimeDays { get; set; } = new[] { 90, 30, 7 };

    /// <summary>For unit-accruing obligations (a CPA's CPE hours, an
    /// attorney's CLE credits, an engineer's PDHs): how many units the
    /// licensing period requires. Null = a date-only renewal (MagCPA
    /// Requirement 3, person-level compliance as a first-class pattern).</summary>
    public decimal? RequiredUnits { get; set; }

    /// <summary>Units completed so far this cycle; resets when the
    /// commitment renews into its next cycle.</summary>
    public decimal CompletedUnits { get; set; }

    /// <summary>What a unit is ("CPE hours", "CLE credits").</summary>
    public string? UnitLabel { get; set; }

    /// <summary>Milestone-gated obligations (Rosetta Requirement 5): a
    /// building permit or occupancy inspection is tied to project phase
    /// completion, not a recurring date. When set, the calendar ladder
    /// stays SILENT until the milestone is marked reached — then the
    /// obligation becomes live and escalates like any other.</summary>
    public string? MilestoneReference { get; set; }

    /// <summary>When the gating milestone was marked reached; null while
    /// the obligation is still dormant behind its milestone.</summary>
    public DateTimeOffset? MilestoneReachedAt { get; set; }

    /// <summary>Role notified as the due date approaches.</summary>
    public string NotifyRole { get; set; } = "ExecutiveOwner";

    /// <summary>Also open a renewal task when the ladder escalates to Warning.</summary>
    public bool CreateTask { get; set; } = true;

    /// <summary>Active | Renewed | Cancelled. Only Active is monitored.</summary>
    public string Status { get; set; } = RenewalStatuses.Active;

    /// <summary>Deepest ladder rung already alerted this cycle (days), or
    /// -1 once the overdue alarm has fired; null when nothing fired yet.
    /// Reset when the commitment is renewed into its next cycle.</summary>
    public int? LastAlertedThresholdDays { get; set; }

    /// <summary>How many cycles this commitment has been renewed.</summary>
    public int RenewalCount { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? LastRenewedAt { get; set; }
}

public static class RenewalCategories
{
    public const string License = "License";
    public const string Insurance = "Insurance";
    public const string Loan = "Loan";
    public const string Lease = "Lease";
    public const string Corporate = "Corporate";
    public const string Contract = "Contract";
    public const string Other = "Other";

    public static readonly string[] All =
        { License, Insurance, Loan, Lease, Corporate, Contract, Other };
}

public static class RenewalStatuses
{
    public const string Active = "Active";
    public const string Renewed = "Renewed";
    public const string Cancelled = "Cancelled";
}
