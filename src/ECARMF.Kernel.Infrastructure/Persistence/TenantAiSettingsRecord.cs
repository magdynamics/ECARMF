using ECARMF.Kernel.Application.Advisor;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class TenantAiSettingsRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The tenant's API key, protected at rest via Data Protection —
    /// never stored or returned in clear text.</summary>
    public string ProtectedApiKey { get; set; } = string.Empty;

    /// <summary>Masked hint (last 4 characters) for the settings UI.</summary>
    public string ApiKeyHint { get; set; } = string.Empty;

    public string? Model { get; set; }
    public string ConfiguredBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public class EfTenantAiSettingsStore : ITenantAiSettingsStore
{
    private const string ProtectorPurpose = "ECARMF.TenantAiSettings.v1";

    private readonly ECARMFDbContext _db;
    private readonly IDataProtector _protector;

    public EfTenantAiSettingsStore(ECARMFDbContext db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public async Task<(string? ApiKey, string? Model)> GetCredentialsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (record is null)
        {
            return (null, null);
        }

        try
        {
            return (_protector.Unprotect(record.ProtectedApiKey), record.Model);
        }
        catch (Exception)
        {
            // Key ring rotated/lost: treat as unconfigured rather than failing
            // the pipeline; the tenant re-enters the key via settings.
            return (null, record.Model);
        }
    }

    public async Task<TenantAiSettingsStatus> GetStatusAsync(string tenantId, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        return record is null
            ? new TenantAiSettingsStatus(false, null, null, null, null)
            : new TenantAiSettingsStatus(true, record.Model, record.ApiKeyHint, record.ConfiguredBy, record.UpdatedAt);
    }

    public async Task SetAsync(
        string tenantId, string apiKey, string? model, string configuredBy, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (record is null)
        {
            record = new TenantAiSettingsRecord { Id = Guid.NewGuid(), TenantId = tenantId };
            _db.TenantAiSettings.Add(record);
        }

        record.ProtectedApiKey = _protector.Protect(apiKey);
        record.ApiKeyHint = apiKey.Length >= 4 ? $"…{apiKey[^4..]}" : "…";
        record.Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        record.ConfiguredBy = configuredBy;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string tenantId, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (record is not null)
        {
            _db.TenantAiSettings.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }
}
