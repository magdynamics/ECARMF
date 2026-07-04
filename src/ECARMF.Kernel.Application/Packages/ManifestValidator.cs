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

            if (!Enum.IsDefined(rule.OutcomeOnMatch))
                errors.Add($"Rule '{label}' has an invalid OutcomeOnMatch value.");

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
