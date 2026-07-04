using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IWorkflowRegistry : IRegistry<WorkflowDeclaration>
{
    IReadOnlyList<Registered<WorkflowDeclaration>> GetWorkflowsForEvent(string eventName);
}

/// <summary>Catalog of automation workflows contributed by active Knowledge
/// Packages — the seventh kernel registry.</summary>
public class WorkflowRegistry : RegistryBase<WorkflowDeclaration>, IWorkflowRegistry
{
    protected override string GetName(WorkflowDeclaration declaration) => declaration.WorkflowId;

    public IReadOnlyList<Registered<WorkflowDeclaration>> GetWorkflowsForEvent(string eventName) =>
        Where(w => string.Equals(w.Declaration.TriggerEvent, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Declaration.WorkflowId, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
