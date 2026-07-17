using System.Text;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Application.Ingestion;

/// <summary>
/// The one ingestion mechanism behind every connector. Resolves the
/// connector's SchemaTemplate from the tenant's registry (active packages
/// only), maps the raw payload declaratively, stamps every record with
/// sourceId / sourceType / provenance / reliabilityRating / ingestedAt,
/// and hands it to the same immutable intake pipeline all records use.
/// </summary>
public class ConnectorIngestionService : IDataSourceConnector
{
    private readonly IConnectorStore _connectors;
    private readonly ITenantRegistryProvider _registries;
    private readonly ITransactionIntakeService _intake;
    private readonly IDocumentLibrary? _library;
    private readonly IOrgUnitStore? _units;

    public ConnectorIngestionService(
        IConnectorStore connectors,
        ITenantRegistryProvider registries,
        ITransactionIntakeService intake,
        IDocumentLibrary? library = null,
        IOrgUnitStore? units = null)
    {
        _connectors = connectors;
        _registries = registries;
        _intake = intake;
        _library = library;
        _units = units;
    }

    public async Task<IngestionResult> IngestAsync(
        string tenantId, string connectorId, string rawPayload, string actorIdentifier,
        string? unitRef = null, CancellationToken ct = default)
    {
        // Unit-scoped data integrity: an attributed unit must exist and be
        // Active — a feed can never file data into a unit that isn't real.
        if (_units is not null)
        {
            var unitError = await UnitScope.ValidateAsync(_units, tenantId, unitRef, ct);
            if (unitError is not null)
            {
                return new IngestionResult(false, [], [], [unitError]);
            }
        }

        var connector = await _connectors.GetAsync(tenantId, connectorId, ct);
        if (connector is null)
        {
            return new IngestionResult(false, [], [], [$"Connector '{connectorId}' is not configured for this tenant."]);
        }

        if (!string.Equals(connector.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return new IngestionResult(false, [], [], [$"Connector '{connectorId}' is {connector.Status}."]);
        }

        var templates = _registries.GetFor(tenantId).SchemaTemplates;
        if (!templates.TryGet(connector.SchemaTemplateId, out var registered))
        {
            return new IngestionResult(false, [], [],
                [$"Schema template '{connector.SchemaTemplateId}' is not registered by any active package of this tenant."]);
        }

        var mapping = SchemaMapper.Map(registered.Declaration, rawPayload);
        if (!mapping.Success)
        {
            await ArchiveAsync(tenantId, connector, registered.Declaration.SourceFormat,
                registered.Declaration.TemplateId, rawPayload, actorIdentifier, [], mapping.Errors, unitRef, ct);
            return new IngestionResult(false, [], [], mapping.Errors);
        }

        var recordIds = new List<Guid>();
        var warnings = new List<string>();

        foreach (var mapped in mapping.Records)
        {
            warnings.AddRange(mapped.Warnings);

            // Ingestion stamp: applied before validation, on every record,
            // regardless of connector. Template-mapped values win over the
            // connector defaults (e.g. a form-supplied reliabilityRating).
            var payload = new Dictionary<string, string>(mapped.Payload, StringComparer.OrdinalIgnoreCase);
            payload["sourceId"] = connector.ConnectorId;
            payload["sourceCategory"] = connector.DomainTag; // legacy key kept for rule compatibility
            payload["domainTag"] = connector.DomainTag;
            payload.TryAdd("sourceType", connector.DomainTag);
            payload["provenance"] = connector.ProvenanceClass;
            payload.TryAdd("reliabilityRating", connector.ReliabilityRating.ToString(System.Globalization.CultureInfo.InvariantCulture));
            payload["ingestedAt"] = DateTimeOffset.UtcNow.ToString("O");
            payload["schemaTemplateId"] = connector.SchemaTemplateId;
            payload["schemaTemplateVersion"] = registered.PackageVersion;

            // Unit attribution: the caller's declared unit (an integration's
            // binding or the upload form) wins; otherwise a template-mapped
            // unitRef in the source data stands; otherwise tenant-wide.
            var effectiveUnit = !string.IsNullOrWhiteSpace(unitRef)
                ? unitRef.Trim()
                : (payload.TryGetValue("unitRef", out var mappedUnit) && !string.IsNullOrWhiteSpace(mappedUnit) ? mappedUnit : null);

            var receipt = await _intake.ReceiveAsync(new TransactionSubmission(
                tenantId, registered.Declaration.TargetEntityType, actorIdentifier, payload,
                UnitRef: effectiveUnit), ct);

            recordIds.Add(receipt.TransactionId);
            if (!receipt.EventPublished && receipt.Note is not null)
            {
                warnings.Add(receipt.Note);
            }
        }

        await ArchiveAsync(tenantId, connector, registered.Declaration.SourceFormat,
            registered.Declaration.TemplateId, rawPayload, actorIdentifier, recordIds, [], unitRef, ct);

        return new IngestionResult(true, recordIds, warnings, []);
    }

    /// <summary>Every payload that comes through a connector lands in the
    /// tenant's source library, verbatim and indexed — including rejected
    /// ones: failed evidence is still evidence.</summary>
    private async Task ArchiveAsync(
        string tenantId, ConnectorDefinition connector, string sourceFormat, string templateId,
        string rawPayload, string actor, IReadOnlyList<Guid> recordIds, IReadOnlyList<string> errors,
        string? unitRef, CancellationToken ct)
    {
        if (_library is null)
        {
            return;
        }

        var metadata = new Dictionary<string, string>
        {
            ["ingestionMode"] = connector.ArrivalMode, // legacy key kept
            ["arrivalMode"] = connector.ArrivalMode,
            ["provenance"] = connector.ProvenanceClass,
            ["accepted"] = (errors.Count == 0).ToString()
        };
        if (errors.Count > 0)
        {
            metadata["errors"] = string.Join("; ", errors);
        }

        await _library.ArchiveAsync(new SourceDocument
        {
            TenantId = tenantId,
            FileName = $"{connector.ConnectorId}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{sourceFormat}",
            MediaType = sourceFormat,
            SourceId = connector.ConnectorId,
            SourceCategory = connector.DomainTag,
            UploadedBy = actor,
            UnitRef = string.IsNullOrWhiteSpace(unitRef) ? null : unitRef.Trim(),
            SchemaTemplateId = templateId,
            RecordIds = [.. recordIds],
            Metadata = metadata
        }, Encoding.UTF8.GetBytes(rawPayload), ct);
    }
}
