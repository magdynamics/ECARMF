namespace ECARMF.Kernel.Application.Ingestion;

/// <summary>Record provenance classes. Downstream scoring must never treat
/// AIGenerated output as equivalent to verified ground truth.</summary>
public static class Provenance
{
    public const string HumanEntered = "HumanEntered";
    public const string ExternalSystemVerified = "ExternalSystemVerified";
    public const string AIGenerated = "AIGenerated";
}

/// <summary>
/// A configured data source connector instance: source category + ingestion
/// mode + SchemaTemplate reference + reliability tier + provenance class.
/// Adding a second bank is a new connector instance reusing an existing
/// template — not new code.
/// </summary>
public sealed record ConnectorDefinition(
    string ConnectorId,
    string Name,
    string SourceCategory,
    string IngestionMode,   // manual | push | pull | file | internal
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
