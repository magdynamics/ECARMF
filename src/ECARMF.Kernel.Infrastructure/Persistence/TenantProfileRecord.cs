using System.Text.Json;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class TenantProfileRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string Status { get; set; } = TenantStatus.Active;
    public string? BillingPlanId { get; set; }
    public string SensitivityTier { get; set; } = "Standard";
    public string? Brand { get; set; }
    public string? Segment { get; set; }
    public string? AccentColor { get; set; }
    public bool HandlesPhi { get; set; }
    /// <summary>Terminology dictionary serialised as JSON (nullable = none).</summary>
    public string? TerminologyJson { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfTenantDirectory : ITenantDirectory
{
    private readonly ECARMFDbContext _db;

    public EfTenantDirectory(ECARMFDbContext db) => _db = db;

    public async Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        var record = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<TenantProfile>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(TenantProfile profile, CancellationToken ct = default)
    {
        _db.Tenants.Add(new TenantProfileRecord
        {
            Id = profile.Id,
            TenantId = profile.TenantId,
            Name = profile.Name,
            Industry = profile.Industry,
            ContactName = profile.ContactName,
            ContactEmail = profile.ContactEmail,
            Status = profile.Status,
            BillingPlanId = profile.BillingPlanId,
            SensitivityTier = profile.SensitivityTier,
            Brand = profile.Brand,
            Segment = profile.Segment,
            AccentColor = profile.AccentColor,
            HandlesPhi = profile.HandlesPhi,
            TerminologyJson = SerializeTerms(profile.Terminology),
            Notes = profile.Notes,
            CreatedBy = profile.CreatedBy,
            CreatedAt = profile.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TenantProfile profile, CancellationToken ct = default)
    {
        var record = await _db.Tenants.FirstAsync(t => t.TenantId == profile.TenantId, ct);
        record.Name = profile.Name;
        record.Industry = profile.Industry;
        record.ContactName = profile.ContactName;
        record.ContactEmail = profile.ContactEmail;
        record.Status = profile.Status;
        record.BillingPlanId = profile.BillingPlanId;
        record.SensitivityTier = profile.SensitivityTier;
        record.Brand = profile.Brand;
        record.Segment = profile.Segment;
        record.AccentColor = profile.AccentColor;
        record.HandlesPhi = profile.HandlesPhi;
        record.TerminologyJson = SerializeTerms(profile.Terminology);
        record.Notes = profile.Notes;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string? SerializeTerms(Dictionary<string, string>? terms) =>
        terms is null || terms.Count == 0 ? null : JsonSerializer.Serialize(terms);

    private static Dictionary<string, string> DeserializeTerms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static TenantProfile ToDomain(TenantProfileRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Name = record.Name,
        Industry = record.Industry,
        ContactName = record.ContactName,
        ContactEmail = record.ContactEmail,
        Status = record.Status,
        BillingPlanId = record.BillingPlanId,
        SensitivityTier = record.SensitivityTier,
        Brand = record.Brand,
        Segment = record.Segment,
        AccentColor = record.AccentColor,
        HandlesPhi = record.HandlesPhi,
        Terminology = DeserializeTerms(record.TerminologyJson),
        Notes = record.Notes,
        CreatedBy = record.CreatedBy,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}
