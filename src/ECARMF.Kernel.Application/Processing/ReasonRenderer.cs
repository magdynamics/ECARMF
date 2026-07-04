using System.Text.RegularExpressions;

namespace ECARMF.Kernel.Application.Processing;

/// <summary>Renders a rule's ReasonTemplate by substituting {field} tokens
/// with payload values. Unknown tokens are left intact so a template typo is
/// visible in the audit trail rather than hidden.</summary>
public static partial class ReasonRenderer
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenPattern();

    public static string Render(string template, IReadOnlyDictionary<string, string> payload)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return TokenPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            var value = payload.FirstOrDefault(kv =>
                string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
            return value ?? match.Value;
        });
    }
}
