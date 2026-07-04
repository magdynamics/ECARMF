using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Ingestion;

/// <summary>One mapped record plus any data-quality findings.</summary>
public sealed record MappedRecord(
    Dictionary<string, string> Payload,
    IReadOnlyList<string> Warnings);

public sealed record MappingResult(
    bool Success,
    IReadOnlyList<MappedRecord> Records,
    IReadOnlyList<string> Errors);

/// <summary>
/// Executes a SchemaTemplate against a raw payload — the mapping is pure
/// metadata; this class is only the mechanism. json maps one record (paths
/// with dots and [n] indexes), csv maps one record per data row, text maps
/// one record via regex patterns over the whole payload. Failed required
/// fields are errors; failed consistency checks set dataValidationFlag
/// instead of rejecting (flag, don't silently drop).
/// </summary>
public static class SchemaMapper
{
    public static MappingResult Map(SchemaTemplateDeclaration template, string rawPayload)
    {
        try
        {
            var rawRecords = template.SourceFormat.ToLowerInvariant() switch
            {
                "json" => [ExtractJson(rawPayload, template)],
                "csv" => ExtractCsv(rawPayload, template),
                "text" => [ExtractText(rawPayload, template)],
                _ => throw new InvalidOperationException($"Unsupported SourceFormat '{template.SourceFormat}'.")
            };

            var records = new List<MappedRecord>();
            var errors = new List<string>();

            foreach (var raw in rawRecords)
            {
                var warnings = new List<string>();
                var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var mapping in template.FieldMappings)
                {
                    raw.TryGetValue(MappingKey(mapping), out var value);

                    if (string.IsNullOrEmpty(value))
                    {
                        if (mapping.Required)
                        {
                            errors.Add($"Required field '{mapping.TargetField}' missing (raw '{mapping.RawField}').");
                        }
                        continue;
                    }

                    var (transformed, warning) = ApplyTransform(mapping, value);
                    if (warning is not null)
                    {
                        warnings.Add(warning);
                    }
                    payload[mapping.TargetField] = transformed;
                }

                foreach (var check in template.ConsistencyChecks)
                {
                    if (!TryDecimal(payload, check.FactorA, out var a)
                        || !TryDecimal(payload, check.FactorB, out var b)
                        || !TryDecimal(payload, check.Product, out var product))
                    {
                        continue;
                    }

                    var expected = a * b;
                    var tolerance = Math.Abs(expected) * check.TolerancePercent / 100m;
                    if (Math.Abs(expected - product) > tolerance)
                    {
                        var flag = $"{check.FactorA}*{check.FactorB}={expected} but {check.Product}={product} (tolerance {check.TolerancePercent}%)";
                        payload["dataValidationFlag"] = flag;
                        warnings.Add($"Consistency check failed: {flag}");
                    }
                }

                records.Add(new MappedRecord(payload, warnings));
            }

            return new MappingResult(errors.Count == 0, records, errors);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or RegexMatchTimeoutException)
        {
            return new MappingResult(false, [], [$"Raw payload could not be parsed as {template.SourceFormat}: {ex.Message}"]);
        }
    }

    private static string MappingKey(FieldMapping mapping) =>
        string.IsNullOrWhiteSpace(mapping.RawField) ? mapping.TargetField : mapping.RawField;

    private static Dictionary<string, string> ExtractJson(string rawPayload, SchemaTemplateDeclaration template)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in template.FieldMappings)
        {
            var value = ResolvePath(document.RootElement, mapping.RawField);
            if (value is not null)
            {
                raw[mapping.RawField] = value;
            }
        }

        return raw;
    }

    /// <summary>Resolves a dotted path with optional [n] indexes, e.g. "Line[1].Amount".</summary>
    private static string? ResolvePath(JsonElement element, string path)
    {
        var current = element;
        foreach (var segment in path.Split('.'))
        {
            var name = segment;
            int? index = null;
            var bracket = segment.IndexOf('[');
            if (bracket >= 0 && segment.EndsWith(']'))
            {
                name = segment[..bracket];
                index = int.Parse(segment[(bracket + 1)..^1], CultureInfo.InvariantCulture);
            }

            if (name.Length > 0)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                {
                    return null;
                }
            }

            if (index is not null)
            {
                if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index)
                {
                    return null;
                }
                current = current[index.Value];
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText()
        };
    }

    private static List<Dictionary<string, string>> ExtractCsv(string rawPayload, SchemaTemplateDeclaration template)
    {
        var lines = rawPayload.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            throw new InvalidOperationException("CSV payload needs a header row and at least one data row.");
        }

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var records = new List<Dictionary<string, string>>();

        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < cells.Length; i++)
            {
                raw[headers[i]] = cells[i].Trim();
            }
            records.Add(raw);
        }

        return records;
    }

    private static Dictionary<string, string> ExtractText(string rawPayload, SchemaTemplateDeclaration template)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in template.FieldMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Pattern))
            {
                continue;
            }

            var match = Regex.Match(rawPayload, mapping.Pattern,
                RegexOptions.Multiline, TimeSpan.FromSeconds(1));
            if (match.Success && match.Groups.Count > 1)
            {
                raw[MappingKey(mapping)] = match.Groups[1].Value;
            }
        }

        return raw;
    }

    private static (string Value, string? Warning) ApplyTransform(FieldMapping mapping, string value)
    {
        switch (mapping.Transform)
        {
            case "parseDate":
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    return (date.UtcDateTime.ToString("O"), null);
                if (DateTime.TryParseExact(value, "yyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var swift))
                    return (swift.ToString("O"), null);
                return (value, $"Could not parse date '{value}' for '{mapping.TargetField}'.");

            case "parseDecimalComma":
                var normalized = value.Replace(".", "").Replace(',', '.');
                return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)
                    ? (dec.ToString(CultureInfo.InvariantCulture), null)
                    : (value, $"Could not parse decimal '{value}' for '{mapping.TargetField}'.");

            case "absoluteValue":
                return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var num)
                    ? (Math.Abs(num).ToString(CultureInfo.InvariantCulture), null)
                    : (value, $"Could not parse number '{value}' for '{mapping.TargetField}'.");

            case "valueMap":
                if (mapping.ValueMap.FirstOrDefault(kv =>
                        string.Equals(kv.Key, value, StringComparison.OrdinalIgnoreCase)).Value is { } mapped)
                    return (mapped, null);
                return (value, $"Value '{value}' not in value map for '{mapping.TargetField}'; kept raw.");

            case "extractRegex":
                if (!string.IsNullOrWhiteSpace(mapping.Pattern))
                {
                    var match = Regex.Match(value, mapping.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                    if (match.Success && match.Groups.Count > 1)
                        return (match.Groups[1].Value, null);
                }
                return (value, $"Pattern did not match for '{mapping.TargetField}'; kept raw.");

            default:
                return (value, null);
        }
    }

    private static bool TryDecimal(Dictionary<string, string> payload, string field, out decimal value)
    {
        value = 0;
        return payload.TryGetValue(field, out var raw)
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
