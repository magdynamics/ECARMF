namespace ECARMF.Kernel.Domain.Knowledge;

/// <summary>
/// A tenant-registered external reference — an authoritative link (a state
/// registry lookup, a public-records portal, a regulator's site) that agents
/// can cite and humans can open. Unlike a package-shipped KnowledgeAsset (which
/// carries retrievable content the model grounds on), a ReferenceSource is a
/// POINTER: the platform surfaces it as an authoritative source to consult,
/// but does not fetch it live. This is the ad-hoc, no-JSON way to add the kind
/// of link an operator finds day to day (e.g. the Illinois SOS business-entity
/// search) without authoring a package.
/// </summary>
public class ReferenceSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>What the source is (e.g. "Illinois SOS — Business Entity Search").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The link. Validated http/https at the door.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Issuing body (e.g. "Illinois Secretary of State", "IRS", "HHS OCR").</summary>
    public string? Issuer { get; set; }

    /// <summary>Jurisdiction the source is authoritative for (e.g. "Illinois",
    /// "Federal"); null when jurisdiction-neutral.</summary>
    public string? Jurisdiction { get; set; }

    /// <summary>Open classification: StateRegistry, PublicRecords, Regulator,
    /// TaxAuthority, IndustryStandard — or any label the tenant uses.</summary>
    public string Category { get; set; } = "ReferenceSource";

    /// <summary>What it's for and how to use it — this is what an agent reads
    /// and relays to a human ("verify the entity here before onboarding").</summary>
    public string? Description { get; set; }

    public string AddedBy { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
