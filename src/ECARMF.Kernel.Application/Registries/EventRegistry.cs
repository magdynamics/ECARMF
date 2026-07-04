using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IEventRegistry : IRegistry<EventDeclaration>
{
    /// <summary>True when an active package has declared the event. The engine
    /// refuses to publish events no package has declared.</summary>
    bool IsDeclared(string eventName);
}

/// <summary>Catalog of event types contributed by active Knowledge Packages.</summary>
public class EventRegistry : RegistryBase<EventDeclaration>, IEventRegistry
{
    protected override string GetName(EventDeclaration declaration) => declaration.EventName;

    public bool IsDeclared(string eventName) => Contains(eventName);
}
