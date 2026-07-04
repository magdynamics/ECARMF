namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// Declarative, versioned mapping from a raw source shape to a registered
/// target entity. Templates ship inside Knowledge Packages and version
/// through PackageLoader — a mapping update is a new version, never a silent
/// change underneath records already processed with the old one.
/// </summary>
public class SchemaTemplateDeclaration
{
    public string TemplateId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Raw input format: json | csv | text.</summary>
    public string SourceFormat { get; set; } = "json";

    /// <summary>Registered entity type this template produces (EntityRegistry).</summary>
    public string TargetEntityType { get; set; } = string.Empty;

    public List<FieldMapping> FieldMappings { get; set; } = [];

    /// <summary>Declarative cross-field validations (e.g. broker
    /// Quantity*Price ≈ Amount). A failed check flags the record for
    /// DataValidation instead of rejecting it.</summary>
    public List<ConsistencyCheck> ConsistencyChecks { get; set; } = [];
}

/// <summary>One raw-field → target-field rule with an optional transform.</summary>
public class FieldMapping
{
    /// <summary>JSON path (dots + [n] indexes) for json, column name for csv.
    /// Ignored for text format, where Pattern extracts from the whole payload.</summary>
    public string RawField { get; set; } = string.Empty;

    public string TargetField { get; set; } = string.Empty;

    /// <summary>none | parseDate | parseDecimalComma | absoluteValue |
    /// valueMap | extractRegex.</summary>
    public string Transform { get; set; } = "none";

    /// <summary>Lookup table for the valueMap transform (e.g. BUY → Purchase).</summary>
    public Dictionary<string, string> ValueMap { get; set; } = [];

    /// <summary>Regex with one capture group for extractRegex, and the
    /// extraction pattern for text-format sources.</summary>
    public string? Pattern { get; set; }

    public bool Required { get; set; }
}

/// <summary>Declares that fieldA * fieldB must approximately equal product
/// (within tolerancePercent); mismatches set dataValidationFlag.</summary>
public class ConsistencyCheck
{
    public string FactorA { get; set; } = string.Empty;
    public string FactorB { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public decimal TolerancePercent { get; set; } = 1m;
}
