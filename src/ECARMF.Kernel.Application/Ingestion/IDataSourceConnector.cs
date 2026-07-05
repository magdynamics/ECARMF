namespace ECARMF.Kernel.Application.Ingestion;

/// <summary>Record provenance classes. Downstream scoring must never treat
/// AIGenerated output as equivalent to verified ground truth.</summary>
public static class Provenance
{
    public const string HumanEntered = "HumanEntered";
    public const string ExternalSystemVerified = "ExternalSystemVerified";
    public const string AIGenerated = "AIGenerated";
}

/// <summary>How data physically arrives (Batch 1, Refinement 3): the FIXED
/// small set — unlike the domain tag, a new arrival mode is genuinely new
/// kernel transport capability, not just a new label.</summary>
public static class ArrivalModes
{
    public const string Push = "Push";
    public const string Pull = "Pull";
    public const string Manual = "Manual";
    public const string File = "File";
    public const string Stream = "Stream";

    public static readonly string[] All = [Push, Pull, Manual, File, Stream];

    /// <summary>Canonicalizes legacy lowercase values ("manual", "internal")
    /// so pre-refinement connector rows read back cleanly.</summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "push" or "internal" => Push, // internal flywheel re-entry is a push
        "pull" => Pull,
        "manual" => Manual,
        "file" => File,
        "stream" => Stream,
        _ => value ?? Manual
    };
}

/// <summary>
/// A configured data source connector instance (Batch 1, Refinement 3):
/// arrival mode (fixed small set) + domain tag (OPEN string — Banking,
/// AccountingSystem, POS, Communications, or any future tag; the original
/// six categories are tag values, never structure) + SchemaTemplate
/// reference + reliability tier + provenance class. Adding a second bank
/// is a new connector instance reusing an existing template — not new code.
/// </summary>
public sealed record ConnectorDefinition(
    string ConnectorId,
    string Name,
    string DomainTag,
    string ArrivalMode,
    string SchemaTemplateId,
    decimal ReliabilityRating,
    string ProvenanceClass,
    string Status);

/// <summary>Tenant-scoped connector configuration store. Seeds the live
/// ManualEntryConnector so the admin UI form works out of the box.</summary>
public interface IConnectorStore
{
    Task<ConnectorDefinition?> GetAsync(string tenantId, string connectorId, CancellationToken ct = default);

    Task<IReadOnlyList<ConnectorDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default);

    Task AddAsync(string tenantId, ConnectorDefinition connector, CancellationToken ct = default);

    Task EnsureSeedConnectorsAsync(string tenantId, CancellationToken ct = default);
}

public static class SeedConnectors
{
    /// <summary>The live manual-entry connector backing the admin UI form.</summary>
    public const string ManualEntry = "manual-entry";
}

/// <summary>The generic connector contract: raw payload in, mapped + stamped
/// records into the one intake pipeline. Push/pull/file connectors implement
/// this same contract later without kernel changes.</summary>
public interface IDataSourceConnector
{
    Task<IngestionResult> IngestAsync(
        string tenantId, string connectorId, string rawPayload, string actorIdentifier, CancellationToken ct = default);
}

public sealed record IngestionResult(
    bool Success,
    IReadOnlyList<Guid> RecordIds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
