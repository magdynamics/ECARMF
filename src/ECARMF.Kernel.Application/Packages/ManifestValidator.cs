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
            // Self-dependency (TCEL P1.1): a package depending on itself is an
            // unsatisfiable declaration and the degenerate 1-node cycle. Caught
            // here in the pure manifest check; multi-package cycles need the
            // stored package set and are detected in PackageLoader.
            else if (!string.IsNullOrWhiteSpace(manifest.PackageId)
                && string.Equals(dependency.PackageId, manifest.PackageId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Package '{manifest.PackageId}' declares a dependency on itself.");
            if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                && !System.Version.TryParse(NormalizeVersion(dependency.MinimumVersion), out _))
                errors.Add($"Dependency '{dependency.PackageId}' has invalid MinimumVersion '{dependency.MinimumVersion}'.");
        }

        // Supersedes / consolidates (TCEL P2.1/P2.3): structural checks only.
        // "Consolidation is real" and "still-active superseded package" are
        // load-time behaviors handled in PackageLoader, not here.
        foreach (var superseded in manifest.Supersedes)
        {
            if (string.IsNullOrWhiteSpace(superseded.PackageId))
                errors.Add("A 'supersedes' entry is missing its PackageId.");
            else if (!string.IsNullOrWhiteSpace(manifest.PackageId)
                && string.Equals(superseded.PackageId, manifest.PackageId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Package '{manifest.PackageId}' cannot supersede itself.");
        }

        foreach (var consolidated in manifest.Consolidates)
        {
            if (string.IsNullOrWhiteSpace(consolidated))
                errors.Add("A 'consolidates' entry is empty.");
            else if (!string.IsNullOrWhiteSpace(manifest.PackageId)
                && string.Equals(consolidated, manifest.PackageId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Package '{manifest.PackageId}' cannot consolidate itself.");
        }

        CheckNames(errors, "entity", manifest.Entities.Select(e => e.EntityTypeName));
        CheckNames(errors, "event", manifest.Events.Select(e => e.EventName));
        CheckNames(errors, "rule", manifest.Rules.Select(r => r.RuleId));
        CheckNames(errors, "capability", manifest.Capabilities.Select(c => c.CapabilityId));
        CheckNames(errors, "schema template", manifest.SchemaTemplates.Select(t => t.TemplateId));
        CheckNames(errors, "performance framework", manifest.PerformanceFrameworks.Select(f => f.FrameworkId));
        CheckNames(errors, "workflow", manifest.Workflows.Select(w => w.WorkflowId));
        CheckNames(errors, "agent", manifest.Agents.Select(a => a.AgentId));
        CheckNames(errors, "knowledge asset", manifest.KnowledgeAssets.Select(a => a.AssetId));
        CheckNames(errors, "AI extraction template", manifest.AiExtractionTemplates.Select(t => t.TemplateId));

        foreach (var extraction in manifest.AiExtractionTemplates)
        {
            var label = string.IsNullOrWhiteSpace(extraction.TemplateId) ? "(unnamed extraction template)" : extraction.TemplateId;
            if (string.IsNullOrWhiteSpace(extraction.TargetType))
                errors.Add($"AI extraction template '{label}' has no TargetType.");
            if (extraction.Fields.Count == 0)
                errors.Add($"AI extraction template '{label}' declares no fields to extract.");
            if (extraction.ReviewThreshold is <= 0 or > 1)
                errors.Add($"AI extraction template '{label}' has ReviewThreshold {extraction.ReviewThreshold} — must be in (0, 1].");
            if (extraction.DocumentKinds.Count == 0)
                errors.Add($"AI extraction template '{label}' declares no DocumentKinds.");
            foreach (var field in extraction.Fields.Where(f => string.IsNullOrWhiteSpace(f.Name)))
                errors.Add($"AI extraction template '{label}' has a field with no Name.");
        }

        foreach (var asset in manifest.KnowledgeAssets)
        {
            var label = string.IsNullOrWhiteSpace(asset.AssetId) ? "(unnamed knowledge asset)" : asset.AssetId;
            if (string.IsNullOrWhiteSpace(asset.DocKey))
                errors.Add($"Knowledge asset '{label}' has no DocKey (the stable identity across versions).");
            if (string.IsNullOrWhiteSpace(asset.Title))
                errors.Add($"Knowledge asset '{label}' has no Title.");
            if (string.IsNullOrWhiteSpace(asset.AssetType))
                errors.Add($"Knowledge asset '{label}' has no AssetType (open: ReferenceManual, PolicyDocument, SOP, ...).");
            // The effective range is the load-bearing feature: tax law changes
            // annually, and an undated asset silently answers with stale rules.
            if (asset.EffectiveFrom == default)
                errors.Add($"Knowledge asset '{label}' has no EffectiveFrom — every version must be effective-dated.");
            if (asset.EffectiveTo is { } to && to <= asset.EffectiveFrom)
                errors.Add($"Knowledge asset '{label}' has EffectiveTo on or before EffectiveFrom.");
            foreach (var relationship in asset.Relationships)
            {
                if (string.IsNullOrWhiteSpace(relationship.RelatedAssetId))
                    errors.Add($"Knowledge asset '{label}' has a relationship with no RelatedAssetId.");
            }
        }

        foreach (var agent in manifest.Agents)
        {
            var label = string.IsNullOrWhiteSpace(agent.AgentId) ? "(unnamed agent)" : agent.AgentId;
            if (string.IsNullOrWhiteSpace(agent.Persona))
                errors.Add($"Agent '{label}' has no Persona — an agent without domain knowledge is just a chat box.");
            foreach (var source in agent.ContextSources)
            {
                var valid = source.ToLowerInvariant() is "scores" or "deviations" or "benchmarks"
                    or "tasks" or "allocations" or "library" or "references"
                    || source.StartsWith("records:", StringComparison.OrdinalIgnoreCase);
                if (!valid)
                    errors.Add($"Agent '{label}' declares invalid context source '{source}' (scores|deviations|benchmarks|tasks|allocations|library|references|records:{{RecordType}}).");
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

    /// <summary>
    /// Non-blocking, manifest-level advisories (TCEL). Kept separate from
    /// Validate so warnings never leak into the error list that blocks a load.
    /// Currently: an agent missing its Owner (P2.2 Identity block). Heuristics
    /// that need the stored/active package set (overlap, consolidation) live in
    /// PackageLoader, not here.
    /// </summary>
    public static IReadOnlyList<string> CollectWarnings(KnowledgePackageManifest manifest)
    {
        var warnings = new List<string>();

        foreach (var agent in manifest.Agents)
        {
            var label = string.IsNullOrWhiteSpace(agent.AgentId) ? "(unnamed agent)" : agent.AgentId;
            if (string.IsNullOrWhiteSpace(agent.Owner))
                warnings.Add($"Agent '{label}' has no Owner — fill in the Identity block (Owner, IndependentValidator, RiskTier).");
        }

        return warnings;
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
