namespace ECARMF.Kernel.Domain.Packages;

/// <summary>One automation step. Step types are kernel mechanisms; what
/// triggers them and what they say is package metadata.</summary>
public class WorkflowStep
{
    /// <summary>notify | createTask | publishEvent.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Role or user identifier the step targets (notify/createTask),
    /// or the event name for publishEvent.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Message/title template with {field} payload tokens.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Info | Warning | Critical (createTask/notify).</summary>
    public string Severity { get; set; } = "Info";
}

/// <summary>
/// Declarative automation: when TriggerEvent fires and Conditions hold,
/// execute Steps — "if lien discovered → notify legal, open a task" as
/// package metadata, never kernel code. The kernel executes; packages decide.
/// </summary>
public class WorkflowDeclaration
{
    public string WorkflowId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string TriggerEvent { get; set; } = string.Empty;

    /// <summary>All must hold (AND); empty means always run on the trigger.</summary>
    public List<RuleCondition> Conditions { get; set; } = [];

    public List<WorkflowStep> Steps { get; set; } = [];
}
