namespace ECARMF.Kernel.Application.Packages;

/// <summary>How a skill is packaged commercially — the platform admin's call.</summary>
public static class SkillPackaging
{
    /// <summary>Bundled into the tenant's core/industry package; not billed
    /// separately.</summary>
    public const string Essential = "Essential";

    /// <summary>Sold à la carte — a separate recurring charge when active.</summary>
    public const string AlaCarte = "AlaCarte";

    public static readonly string[] All = [Essential, AlaCarte];

    public static bool IsValid(string? value) =>
        All.Any(v => string.Equals(v, value, System.StringComparison.OrdinalIgnoreCase));
}

/// <summary>Platform-admin override of a skill's packaging and price. Absent =
/// the code-defined default (add-ons à la carte, everything else essential).</summary>
public sealed record SkillSetting(string SkillId, string Packaging, decimal MonthlyPrice);

/// <summary>Persistence for skill packaging overrides, keyed by skill id
/// (= package id). Platform-wide (not per tenant).</summary>
public interface ISkillSettingStore
{
    Task<IReadOnlyDictionary<string, SkillSetting>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(SkillSetting setting, string actor, CancellationToken ct = default);
}
