using ECARMF.Kernel.Application.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfConnectorStore : IConnectorStore
{
    private readonly ECARMFDbContext _db;

    public EfConnectorStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task<ConnectorDefinition?> GetAsync(string tenantId, string connectorId, CancellationToken ct = default)
    {
        var record = await _db.Connectors.AsNoTracking().FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.ConnectorId == connectorId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<ConnectorDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Connectors.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.ConnectorId)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(string tenantId, ConnectorDefinition connector, CancellationToken ct = default)
    {
        _db.Connectors.Add(ToRecord(tenantId, connector));
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureSeedConnectorsAsync(string tenantId, CancellationToken ct = default)
    {
        if (await _db.Connectors.AnyAsync(c => c.TenantId == tenantId, ct))
        {
            return;
        }

        _db.Connectors.Add(ToRecord(tenantId, new ConnectorDefinition(
            SeedConnectors.ManualEntry,
            "Manual Entry (admin UI form)",
            "Manual",
            ArrivalModes.Manual,
            "manual-opportunity-json",
            0.5m,
            Provenance.HumanEntered,
            "Active")));

        await _db.SaveChangesAsync(ct);
    }

    private static ConnectorRecord ToRecord(string tenantId, ConnectorDefinition c) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ConnectorId = c.ConnectorId,
        Name = c.Name,
        DomainTag = c.DomainTag,
        ArrivalMode = c.ArrivalMode,
        SchemaTemplateId = c.SchemaTemplateId,
        ReliabilityRating = c.ReliabilityRating,
        ProvenanceClass = c.ProvenanceClass,
        Status = c.Status,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ConnectorDefinition ToDomain(ConnectorRecord r) => new(
        r.ConnectorId, r.Name, r.DomainTag, ArrivalModes.Normalize(r.ArrivalMode),
        r.SchemaTemplateId, r.ReliabilityRating, r.ProvenanceClass, r.Status);
}
