using System.Collections.Concurrent;

namespace ECARMF.Kernel.Application.Registries;

/// <summary>The eight kernel registries belonging to one tenant.</summary>
public sealed record TenantRegistries(
    IEntityRegistry Entities,
    IRuleRegistry Rules,
    IEventRegistry Events,
    ICapabilityRegistry Capabilities,
    ISchemaTemplateRegistry SchemaTemplates,
    IPerformanceFrameworkRegistry PerformanceFrameworks,
    IWorkflowRegistry Workflows,
    IAgentRegistry Agents);

/// <summary>
/// Tenant isolation boundary for the in-memory runtime. Each tenant gets its
/// own registry set, so one client's packages, rules, and events can never
/// observe or conflict with another client's.
/// </summary>
public interface ITenantRegistryProvider
{
    TenantRegistries GetFor(string tenantId);
}

public class TenantRegistryProvider : ITenantRegistryProvider
{
    private readonly ConcurrentDictionary<string, TenantRegistries> _tenants =
        new(StringComparer.OrdinalIgnoreCase);

    public TenantRegistries GetFor(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        return _tenants.GetOrAdd(tenantId, _ => new TenantRegistries(
            new EntityRegistry(),
            new RuleRegistry(),
            new EventRegistry(),
            new CapabilityRegistry(),
            new SchemaTemplateRegistry(),
            new PerformanceFrameworkRegistry(),
            new WorkflowRegistry(),
            new AgentRegistry()));
    }
}
