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
        if (await _db.Users.AnyAsync(u => u.TenantId == tenantId, ct))
        {
            return;
        }

        _db.Users.AddRange(
            NewRecord(tenantId, SeedUsers.Admin, "Platform Administrator", false, [RoleCatalog.PlatformAdministrator]),
            NewRecord(tenantId, SeedUsers.Owner, "Executive / Owner", false, [RoleCatalog.ExecutiveOwner]),
            NewRecord(tenantId, SeedUsers.SystemActor, "Flywheel AI System Actor", true, [RoleCatalog.AISystemActor]));

        await _db.SaveChangesAsync(ct);
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
