using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;

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

    public ConnectorIngestionService(
        IConnectorStore connectors,
        ITenantRegistryProvider registries,
        ITransactionIntakeService intake)
    {
        _connectors = connectors;
        _registries = registries;
        _intake = intake;
    }

    public async Task<IngestionResult> IngestAsync(
        string tenantId, string connectorId, string rawPayload, string actorIdentifier, CancellationToken ct = default)
    {
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
            payload["sourceCategory"] = connector.SourceCategory;
            payload.TryAdd("sourceType", connector.SourceCategory);
            payload["provenance"] = connector.ProvenanceClass;
            payload.TryAdd("reliabilityRating", connector.ReliabilityRating.ToString(System.Globalization.CultureInfo.InvariantCulture));
            payload["ingestedAt"] = DateTimeOffset.UtcNow.ToString("O");
            payload["schemaTemplateId"] = connector.SchemaTemplateId;
            payload["schemaTemplateVersion"] = registered.PackageVersion;

            var receipt = await _intake.ReceiveAsync(new TransactionSubmission(
                tenantId, registered.Declaration.TargetEntityType, actorIdentifier, payload), ct);

            recordIds.Add(receipt.TransactionId);
            if (!receipt.EventPublished && receipt.Note is not null)
            {
                warnings.Add(receipt.Note);
            }
        }

        return new IngestionResult(true, recordIds, warnings, []);
    }
}
