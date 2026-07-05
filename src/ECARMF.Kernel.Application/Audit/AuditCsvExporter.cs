using System.Text;
using ECARMF.Kernel.Domain.Audit;

namespace ECARMF.Kernel.Application.Audit;

/// <summary>
/// Regulator-ready export: the append-only audit trail as a flat CSV an
/// examiner can open anywhere. Every column is verbatim from the trail;
/// the export adds nothing and hides nothing.
/// </summary>
public static class AuditCsvExporter
{
    public static byte[] ToCsv(IReadOnlyList<AuditEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OccurredAtUtc,Category,Actor,Summary,CorrelationId,TenantId,Detail");
        foreach (var entry in entries)
        {
            var detail = string.Join("; ", entry.Detail.Select(kv => $"{kv.Key}={kv.Value}"));
            sb.Append(entry.OccurredAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
              .Append(Quote(entry.Category)).Append(',')
              .Append(Quote(entry.Actor)).Append(',')
              .Append(Quote(entry.Summary)).Append(',')
              .Append(entry.CorrelationId).Append(',')
              .Append(Quote(entry.TenantId)).Append(',')
              .Append(Quote(detail)).AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Quote(string value) =>
        '"' + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';
}
