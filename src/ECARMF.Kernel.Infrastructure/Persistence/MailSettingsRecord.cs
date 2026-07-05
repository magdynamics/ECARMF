using ECARMF.Kernel.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Single-row platform mail configuration; the SMTP password is
/// protected at rest like tenant AI keys.</summary>
public class MailSettingsRecord
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? ProtectedPassword { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string MinSeverity { get; set; } = "Warning";
    public string ConfiguredBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public class EfMailSettingsStore : IMailSettingsStore
{
    private const string ProtectorPurpose = "ECARMF.MailSettings.v1";

    private readonly ECARMFDbContext _db;
    private readonly IDataProtector _protector;

    public EfMailSettingsStore(ECARMFDbContext db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public async Task<MailDeliverySettings?> GetAsync(CancellationToken ct = default)
    {
        var record = await _db.MailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (record is null)
        {
            return null;
        }

        string? password = null;
        if (!string.IsNullOrEmpty(record.ProtectedPassword))
        {
            try
            {
                password = _protector.Unprotect(record.ProtectedPassword);
            }
            catch (Exception)
            {
                // Key ring rotated/lost: treat as passwordless rather than
                // failing every dispatch; the operator re-enters it.
            }
        }

        return new MailDeliverySettings(
            record.Enabled, record.Host, record.Port, record.UseSsl,
            record.Username, password, record.FromAddress, record.MinSeverity);
    }

    public async Task<MailSettingsStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var record = await _db.MailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return record is null
            ? new MailSettingsStatus(false, false, null, null, null, null, null, null, null, null)
            : new MailSettingsStatus(true, record.Enabled, record.Host, record.Port, record.UseSsl,
                record.Username, record.FromAddress, record.MinSeverity, record.ConfiguredBy, record.UpdatedAt);
    }

    public async Task SetAsync(
        MailDeliverySettings settings, string configuredBy, CancellationToken ct = default)
    {
        var record = await _db.MailSettings.FirstOrDefaultAsync(ct);
        if (record is null)
        {
            record = new MailSettingsRecord();
            _db.MailSettings.Add(record);
        }

        record.Enabled = settings.Enabled;
        record.Host = settings.Host;
        record.Port = settings.Port;
        record.UseSsl = settings.UseSsl;
        record.Username = settings.Username;
        // An empty password on save keeps the stored one (edit without re-entry).
        if (!string.IsNullOrEmpty(settings.Password))
        {
            record.ProtectedPassword = _protector.Protect(settings.Password);
        }
        else if (string.IsNullOrWhiteSpace(settings.Username))
        {
            record.ProtectedPassword = null;
        }
        record.FromAddress = settings.FromAddress;
        record.MinSeverity = settings.MinSeverity;
        record.ConfiguredBy = configuredBy;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
