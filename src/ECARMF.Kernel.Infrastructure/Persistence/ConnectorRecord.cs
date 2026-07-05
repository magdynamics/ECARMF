namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ConnectorRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string ConnectorId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DomainTag { get; set; } = string.Empty;

    public string ArrivalMode { get; set; } = string.Empty;

    public string SchemaTemplateId { get; set; } = string.Empty;

    public decimal ReliabilityRating { get; set; }

    public string ProvenanceClass { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
