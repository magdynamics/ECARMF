using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>
/// Validates a Knowledge Package manifest before it is staged. Returns the
/// complete error list rather than failing on the first problem, so a failed
/// load is fully explainable.
/// </summary>
public static class ManifestValidator
{
    public static IReadOnlyList<string> Validate(KnowledgePackageManifest manifest, IEventRegistry eventRegistry)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.PackageId))
            errors.Add("PackageId is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(manifest.PackageVersion))
            errors.Add("PackageVersion is required.");
        else if (!System.Version.TryParse(NormalizeVersion(manifest.PackageVersion), out _))
            errors.Add($"PackageVersion '{manifest.PackageVersion}' is not a valid version (expected major.minor[.patch]).");

        foreach (var dependency in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.PackageId))
                errors.Add("A dependency is missing its PackageId.");
            if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                && !System.Version.TryParse(NormalizeVersion(dependency.MinimumVersion), out _))
                errors.Add($"Dependency '{dependency.PackageId}' has invalid MinimumVersion '{dependency.MinimumVersion}'.");
        }

        CheckNames(errors, "entity", manifest.Entities.Select(e => e.EntityTypeName));
        CheckNames(errors, "event", manifest.Events.Select(e => e.EventName));
        CheckNames(errors, "rule", manifest.Rules.Select(r => r.RuleId));
        CheckNames(errors, "capability", manifest.Capabilities.Select(c => c.CapabilityId));
        CheckNames(errors, "schema template", manifest.SchemaTemplates.Select(t => t.TemplateId));
        CheckNames(errors, "performance framework", manifest.PerformanceFrameworks.Select(f => f.FrameworkId));
        CheckNames(errors, "workflow", manifest.Workflows.Select(w => w.WorkflowId));
        CheckNames(errors, "agent", manifest.Agents.Select(a => a.AgentId));

        foreach (var agent in manifest.Agents)
        {
            var label = string.IsNullOrWhiteSpace(agent.AgentId) ? "(unnamed agent)" : agent.AgentId;
            if (string.IsNullOrWhiteSpace(agent.Persona))
                errors.Add($"Agent '{label}' has no Persona — an agent without domain knowledge is just a chat box.");
            foreach (var source in agent.ContextSources)
            {
                var valid = source.ToLowerInvariant() is "scores" or "deviations" or "benchmarks"
                    or "tasks" or "allocations" or "library"
                    || source.StartsWith("records:", StringComparison.OrdinalIgnoreCase);
                if (!valid)
                    errors.Add($"Agent '{label}' declares invalid context source '{source}' (scores|deviations|benchmarks|tasks|allocations|library|records:{{RecordType}}).");
            }
        }

        foreach (var workflow in manifest.Workflows)
        {
            var label = string.IsNullOrWhiteSpace(workflow.WorkflowId) ? "(unnamed workflow)" : workflow.WorkflowId;
            if (string.IsNullOrWhiteSpace(workflow.TriggerEvent))
                errors.Add($"Workflow '{label}' has no TriggerEvent.");
            if (workflow.Steps.Count == 0)
                errors.Add($"Workflow '{label}' declares no steps.");
            foreach (var step in workflow.Steps)
            {
                if (step.Type.ToLowerInvariant() is not ("notify" or "createtask" or "publishevent"))
                    errors.Add($"Workflow '{label}' has invalid step type '{step.Type}' (notify|createTask|publishEvent).");
                if (string.IsNullOrWhiteSpace(step.Target))
                    errors.Add($"Workflow '{label}' has a step with no Target.");
            }
        }

        foreach (var framework in manifest.PerformanceFrameworks)
        {
            var label = string.IsNullOrWhiteSpace(framework.FrameworkId) ? "(unnamed framework)" : framework.FrameworkId;
            var kpiIds = new HashSet<string>(framework.Kpis.Select(k => k.KpiId), StringComparer.OrdinalIgnoreCase);

            foreach (var kpi in framework.Kpis)
            {
                if (string.IsNullOrWhiteSpace(kpi.KpiId))
                    errors.Add($"Framework '{label}' has a KPI with no KpiId.");
                if (string.IsNullOrWhiteSpace(kpi.Formula))
                    errors.Add($"Framework '{label}' KPI '{kpi.KpiId}' has no Formula.");
            }

            foreach (var okr in framework.Okrs)
            {
                foreach (var kr in okr.KeyResults.Where(kr => !kpiIds.Contains(kr.KpiId)))
                    errors.Add($"Framework '{label}' OKR '{okr.OkrId}' references unknown KPI '{kr.KpiId}'.");
            }
        }

        foreach (var template in manifest.SchemaTemplates)
        {
            var label = string.IsNullOrWhiteSpace(template.TemplateId) ? "(unnamed template)" : template.TemplateId;

            if (string.IsNullOrWhiteSpace(template.TargetEntityType))
                errors.Add($"Schema template '{label}' has no TargetEntityType.");
            if (template.SourceFormat is not ("json" or "csv" or "text"))
                errors.Add($"Schema template '{label}' has invalid SourceFormat '{template.SourceFormat}' (json|csv|text).");
            if (template.FieldMappings.Count == 0)
                errors.Add($"Schema template '{label}' declares no field mappings.");

            foreach (var mapping in template.FieldMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.TargetField))
                    errors.Add($"Schema template '{label}' has a mapping with no TargetField.");
                if (template.SourceFormat != "text" && string.IsNullOrWhiteSpace(mapping.RawField))
                    errors.Add($"Schema template '{label}' has a mapping with no RawField.");
                if (template.SourceFormat == "text" && string.IsNullOrWhiteSpace(mapping.Pattern))
                    errors.Add($"Schema template '{label}' is text-format but mapping '{mapping.TargetField}' has no Pattern.");
            }
        }

        foreach (var entity in manifest.Entities)
        {
            foreach (var attribute in entity.Attributes.Where(a => string.IsNullOrWhiteSpace(a.Name)))
                errors.Add($"Entity '{entity.EntityTypeName}' declares an attribute with no name.");
        }

        var declaredEvents = new HashSet<string>(
            manifest.Events.Select(e => e.EventName), StringComparer.OrdinalIgnoreCase);

        foreach (var rule in manifest.Rules)
        {
            var label = string.IsNullOrWhiteSpace(rule.RuleId) ? "(unnamed rule)" : rule.RuleId;

            if (string.IsNullOrWhiteSpace(rule.TriggerEvent))
            {
                errors.Add($"Rule '{label}' has no TriggerEvent.");
            }
            else if (!declaredEvents.Contains(rule.TriggerEvent) && !eventRegistry.IsDeclared(rule.TriggerEvent))
            {
                errors.Add($"Rule '{label}' triggers on event '{rule.TriggerEvent}', which is not declared by this package or any active package.");
            }

            // A rule must do something when it matches: decide an outcome,
            // emit scores, or both. Scoring-only rules leave OutcomeOnMatch empty.
            if (string.IsNullOrWhiteSpace(rule.OutcomeOnMatch) && rule.EmitScores.Count == 0)
                errors.Add($"Rule '{label}' declares neither an OutcomeOnMatch nor any EmitScores.");

            foreach (var emission in rule.EmitScores)
            {
                if (string.IsNullOrWhiteSpace(emission.ScoreType))
                    errors.Add($"Rule '{label}' has a score emission with no ScoreType.");
                if (string.IsNullOrWhiteSpace(emission.Value))
                    errors.Add($"Rule '{label}' has a score emission with no Value.");
            }

            foreach (var condition in rule.Conditions)
            {
                if (string.IsNullOrWhiteSpace(condition.Field))
                    errors.Add($"Rule '{label}' has a condition with no Field.");
                if (string.IsNullOrWhiteSpace(condition.Value))
                    errors.Add($"Rule '{label}' has a condition with no Value.");
                if (!Enum.IsDefined(condition.Operator))
                    errors.Add($"Rule '{label}' has a condition with an invalid Operator.");
            }
        }

        return errors;
    }

    /// <summary>Pads "1.0" style versions so System.Version accepts one-part versions too.</summary>
    internal static string NormalizeVersion(string version) =>
        version.Contains('.') ? version : version + ".0";

    private static void CheckNames(List<string> errors, string kind, IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                errors.Add($"A {kind} declaration is missing its name.");
            else if (!seen.Add(name))
                errors.Add($"Duplicate {kind} declaration '{name}' within the manifest.");
        }
    }
}
