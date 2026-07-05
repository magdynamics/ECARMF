using ECARMF.Kernel.Application.Advisor;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class TenantAiSettingsRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>anthropic | local (an OpenAI-compatible server on-premise).</summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>Local provider: the on-prem server's base URL (e.g. http://localhost:11434).</summary>
    public string? Endpoint { get; set; }

    /// <summary>The tenant's API key, protected at rest via Data Protection —
    /// never stored or returned in clear text. Optional for local servers.</summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>Masked hint (last 4 characters) for the settings UI.</summary>
    public string? ApiKeyHint { get; set; }

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

    public async Task<TenantAiCredentials?> GetCredentialsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (record is null)
        {
            return null;
        }

        string? apiKey = null;
        if (!string.IsNullOrEmpty(record.ProtectedApiKey))
        {
            try
            {
                apiKey = _protector.Unprotect(record.ProtectedApiKey);
            }
            catch (Exception)
            {
                // Key ring rotated/lost: treat as keyless rather than failing
                // the pipeline; the tenant re-enters the key via settings.
            }
        }

        return new TenantAiCredentials(record.Provider, apiKey, record.Endpoint, record.Model);
    }

    public async Task<TenantAiSettingsStatus> GetStatusAsync(string tenantId, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        return record is null
            ? new TenantAiSettingsStatus(false, null, null, null, null, null, null)
            : new TenantAiSettingsStatus(true, record.Provider, record.Endpoint, record.Model,
                record.ApiKeyHint, record.ConfiguredBy, record.UpdatedAt);
    }

    public async Task SetAsync(
        string tenantId, string provider, string? apiKey, string? endpoint, string? model,
        string configuredBy, CancellationToken ct = default)
    {
        var record = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (record is null)
        {
            record = new TenantAiSettingsRecord { Id = Guid.NewGuid(), TenantId = tenantId };
            _db.TenantAiSettings.Add(record);
        }

        record.Provider = provider;
        record.Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            record.ProtectedApiKey = _protector.Protect(apiKey.Trim());
            record.ApiKeyHint = apiKey.Trim().Length >= 4 ? $"…{apiKey.Trim()[^4..]}" : "…";
        }
        else
        {
            record.ProtectedApiKey = null;
            record.ApiKeyHint = null;
        }
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
