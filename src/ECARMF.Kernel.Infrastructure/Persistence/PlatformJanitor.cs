using ECARMF.Kernel.Application.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>
/// Ghost-tenant purge: discovers every table with a TenantId column from
/// the catalog (so new tables are covered automatically) and deletes the
/// ghost's rows with a parameterized statement per table. Table names come
/// from sys.tables — never from input — and the tenant id is always a
/// parameter, so nothing here is injectable.
/// </summary>
public class PlatformJanitor : IPlatformJanitor
{
    private readonly ECARMFDbContext _db;

    public PlatformJanitor(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, int>> PurgeGhostTenantAsync(
        string tenantId, CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var tables = new List<string>();
        await using (var discover = connection.CreateCommand())
        {
            discover.CommandText =
                "SELECT t.name FROM sys.tables t JOIN sys.columns c " +
                "ON c.object_id = t.object_id AND c.name = 'TenantId'";
            await using var reader = await discover.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var deleted = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM [{table}] WHERE TenantId = @tenantId";
            var parameter = new SqlParameter("@tenantId", tenantId);
            delete.Parameters.Add(parameter);
            var count = await delete.ExecuteNonQueryAsync(ct);
            if (count > 0)
            {
                deleted[table] = count;
            }
        }

        return deleted;
    }
}
