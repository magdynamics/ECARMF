namespace ECARMF.Kernel.Application.Identity;

/// <summary>
/// The one rule of unit-scoped data integrity: data entering the platform is
/// attributed to a REAL, ACTIVE organizational unit, or explicitly to the
/// whole tenant (unitRef = null, e.g. an HR guideline that applies to every
/// location) — never to a unit that doesn't exist. Every ingest door calls
/// this before accepting unit-scoped data.
/// </summary>
public static class UnitScope
{
    /// <summary>Returns an error message when the unit is invalid; null when
    /// the attribution is acceptable (including the tenant-wide case).</summary>
    public static async Task<string?> ValidateAsync(
        IOrgUnitStore units, string tenantId, string? unitRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(unitRef))
        {
            return null; // tenant-wide: applies to all units
        }

        var unit = await units.GetAsync(tenantId, unitRef.Trim(), ct);
        if (unit is null)
        {
            return $"Organizational unit '{unitRef}' does not exist for this tenant. " +
                   "Create it under Organization first, or omit the unit for tenant-wide data.";
        }

        if (!string.Equals(unit.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return $"Organizational unit '{unitRef}' is {unit.Status}; data cannot be attributed to it.";
        }

        return null;
    }
}
