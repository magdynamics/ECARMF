namespace ECARMF.Kernel.Domain.Billing;

/// <summary>
/// How the platform charges a client: a base subscription plus unit prices
/// for metered utilization. Plans are platform-level definitions; each
/// tenant profile is assigned one.
/// </summary>
public class BillingPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable identifier (slug), e.g. "standard", "enterprise".</summary>
    public string PlanId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    public decimal BaseMonthlyFee { get; set; }

    public decimal PricePerRecord { get; set; }

    public decimal PricePerDocumentArchived { get; set; }

    public decimal PricePerAiCall { get; set; }

    public decimal PricePerFeedRun { get; set; }

    public decimal PricePerActiveUser { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Metered utilization of one tenant over a period — every number
/// derives from data the kernel already records, so the meter is honest by
/// construction.</summary>
public sealed record UsageSummary(
    string TenantId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int RecordsProcessed,
    int DocumentsArchived,
    long StorageBytes,
    int AiCalls,
    int FeedRuns,
    int ActiveUsers);

/// <summary>One line of a statement: metric x quantity x unit price.</summary>
public class BillingLineItem
{
    public string Metric { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>A generated utilization statement for one tenant and period.</summary>
public class BillingStatement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public string PlanId { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    public DateTimeOffset PeriodStart { get; set; }

    public DateTimeOffset PeriodEnd { get; set; }

    public List<BillingLineItem> Lines { get; set; } = [];

    public decimal Total { get; set; }

    /// <summary>Draft | Issued.</summary>
    public string Status { get; set; } = "Draft";

    public string GeneratedBy { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
