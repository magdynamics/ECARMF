using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IRuleRegistry : IRegistry<RuleDeclaration>
{
    /// <summary>Rules subscribed to the given event, in execution order
    /// (ascending Priority, then RuleId for determinism).</summary>
    IReadOnlyList<Registered<RuleDeclaration>> GetRulesForEvent(string eventName);
}

/// <summary>Catalog of executable rules contributed by active Knowledge Packages.</summary>
public class RuleRegistry : RegistryBase<RuleDeclaration>, IRuleRegistry
{
    protected override string GetName(RuleDeclaration declaration) => declaration.RuleId;

    public IReadOnlyList<Registered<RuleDeclaration>> GetRulesForEvent(string eventName)
    {
        return Where(r => string.Equals(r.Declaration.TriggerEvent, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Declaration.Priority)
            .ThenBy(r => r.Declaration.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
