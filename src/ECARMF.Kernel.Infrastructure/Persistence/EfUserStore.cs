using System.Text.Json;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfUserStore : IUserStore
{
    private readonly ECARMFDbContext _db;

    public EfUserStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdentifierAsync(string tenantId, string identifier, CancellationToken ct = default)
    {
        var record = await _db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Identifier == identifier, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Identifier)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task EnsureSeedUsersAsync(string tenantId, CancellationToken ct = default)
    {
        // Per-identifier idempotency: tenants seeded before a new well-known
        // actor existed pick it up on their next request.
        var existing = await _db.Users
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Identifier)
            .ToListAsync(ct);

        var missing = new List<UserRecord>();

        void AddIfMissing(string identifier, string displayName, bool isSystemActor, string[] roles)
        {
            if (!existing.Contains(identifier, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(NewRecord(tenantId, identifier, displayName, isSystemActor, roles));
            }
        }

        AddIfMissing(SeedUsers.Admin, "Platform Administrator", false, [RoleCatalog.PlatformAdministrator]);
        AddIfMissing(SeedUsers.Owner, "Executive / Owner", false, [RoleCatalog.ExecutiveOwner]);
        AddIfMissing(SeedUsers.SystemActor, "Flywheel AI System Actor", true, [RoleCatalog.AISystemActor]);
        AddIfMissing(SeedUsers.AdvisorActor, "Executive Advisor AI Agent", true, [RoleCatalog.AISystemActor]);

        if (missing.Count > 0)
        {
            _db.Users.AddRange(missing);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static UserRecord NewRecord(
        string tenantId, string identifier, string displayName, bool isSystemActor, string[] roles) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Identifier = identifier,
        DisplayName = displayName,
        IsSystemActor = isSystemActor,
        Status = "Active",
        RolesJson = JsonSerializer.Serialize(roles),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static User ToDomain(UserRecord record) => new()
    {
        EntityId = record.Id,
        TenantId = record.TenantId,
        EntityType = nameof(User),
        EntityName = record.DisplayName,
        Identifier = record.Identifier,
        DisplayName = record.DisplayName,
        IsSystemActor = record.IsSystemActor,
        Status = record.Status,
        Roles = JsonSerializer.Deserialize<List<string>>(record.RolesJson) ?? []
    };
}
