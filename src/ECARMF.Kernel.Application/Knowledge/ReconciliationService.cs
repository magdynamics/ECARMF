using System.Globalization;
using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Domain.Knowledge;

namespace ECARMF.Kernel.Application.Knowledge;

/// <summary>Persistence for extracted document data (the queryable numbers).</summary>
public interface IExtractedDataStore
{
    Task AddAsync(ExtractedDocumentData data, CancellationToken ct = default);
    Task<IReadOnlyList<ExtractedDocumentData>> GetByTypeAsync(string tenantId, string documentType, CancellationToken ct = default);
}

/// <summary>One source document that fed a reconciliation total — the audit trail.</summary>
public sealed record ReconciliationSource(Guid DocumentId, string FileName, string? Subject, string? Period, decimal Value);

public sealed record ReconciliationResult(
    bool Success,
    string? Error,
    string Interpretation,   // how the AI understood the request
    string DocumentType,
    string Field,
    string Operation,        // sum | count | average
    decimal Value,           // computed by the PLATFORM, not the model
    int DocumentsUsed,
    IReadOnlyList<ReconciliationSource> Sources);

public interface IReconciliationService
{
    /// <summary>Answers a data task ("add all deposits in BOA account 123",
    /// "sum John's W-2 wages for the last 3 years"): the AI parses the request
    /// into a structured query; the platform filters the extracted document
    /// data and computes the aggregate deterministically, returning the total
    /// with every source document behind it.</summary>
    Task<ReconciliationResult> RunAsync(string tenantId, string request, CancellationToken ct = default);
}

public class ReconciliationService : IReconciliationService
{
    private const string SystemPrompt =
        "You translate a plain-English data request about a business's documents into a structured " +
        "query. You do NOT compute anything — you only identify what to fetch and how to aggregate. " +
        "Available document types and their key fields are given. Respond with ONLY JSON: " +
        "{\"documentType\": one of the type keys, \"field\": the aggregatable field to operate on, " +
        "\"operation\": \"sum\"|\"count\"|\"average\", \"subjectContains\": string|null (an account " +
        "number, employee name, or entity the request names), \"periods\": [string] (years/periods " +
        "named or implied, empty if none), \"interpretation\": one sentence restating the request}.";

    private readonly ILanguageModelProvider _llmProvider;
    private readonly IExtractedDataStore _store;

    public ReconciliationService(ILanguageModelProvider llmProvider, IExtractedDataStore store)
    {
        _llmProvider = llmProvider;
        _store = store;
    }

    public async Task<ReconciliationResult> RunAsync(string tenantId, string request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request))
            return Fail("A request is required.");

        var llm = await _llmProvider.GetForTenantAsync(tenantId, ct);
        if (!llm.IsConfigured)
            return Fail("Reconciliation needs this tenant's AI backend to understand the request (Setup → AI Backend).");

        Query? q;
        try
        {
            var typesForPrompt = string.Join("\n", DocumentTypeCatalog.All.Select(t =>
                $"- {t.TypeKey}: fields [{string.Join(", ", t.KeyFields.Where(f => f.Aggregatable).Select(f => f.Name))}]"));
            var raw = await llm.CompleteAsync(SystemPrompt,
                $"Document types and their aggregatable fields:\n{typesForPrompt}\n\nRequest: {request}", ct);
            q = ParseQuery(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail($"Could not understand the request: {ex.Message}");
        }
        if (q is null)
            return Fail("Could not parse the request into a data query — try rephrasing (name the document type, field, and any account/employee).");

        var type = DocumentTypeCatalog.Find(q.DocumentType);
        if (type is null)
            return Fail($"Unknown document type '{q.DocumentType}'.");
        if (!type.KeyFields.Any(f => string.Equals(f.Name, q.Field, StringComparison.OrdinalIgnoreCase) && f.Aggregatable))
            return Fail($"'{q.Field}' is not an aggregatable field of a {type.Name}.");

        // Fetch the extracted data and FILTER deterministically.
        var all = await _store.GetByTypeAsync(tenantId, q.DocumentType, ct);
        var subject = q.SubjectContains?.Trim().ToLowerInvariant();
        var matched = all.Where(d =>
            (string.IsNullOrWhiteSpace(subject)
                || (d.SubjectKey?.Contains(subject, StringComparison.OrdinalIgnoreCase) ?? false)
                || d.Fields.Values.Any(v => v.Contains(subject, StringComparison.OrdinalIgnoreCase)))
            && (q.Periods.Count == 0 || q.Periods.Any(p =>
                (d.Period?.Contains(p, StringComparison.OrdinalIgnoreCase) ?? false))))
            .ToList();

        // The PLATFORM computes — never the model. Field lookup is tolerant of
        // the naming a small model actually produces (depositsTotal vs.
        // totalDeposits vs. deposits): match on the normalized field name.
        var sources = new List<ReconciliationSource>();
        foreach (var d in matched)
        {
            var value = FindFieldValue(d.Fields, q.Field);
            sources.Add(new ReconciliationSource(d.DocumentId, d.FileName, d.SubjectKey, d.Period, value));
        }

        var op = q.Operation.ToLowerInvariant();
        var computed = op switch
        {
            "count" => sources.Count,
            "average" => sources.Count > 0 ? Math.Round(sources.Average(s => s.Value), 2) : 0m,
            _ => Math.Round(sources.Sum(s => s.Value), 2), // sum (default)
        };

        return new ReconciliationResult(
            true, null, q.Interpretation, q.DocumentType, q.Field, op == "count" || op == "average" ? op : "sum",
            computed, sources.Count, sources);
    }

    /// <summary>Reads a numeric field tolerantly: exact key first, then a
    /// normalized match (case- and order-insensitive on the alphanumerics), so
    /// a model's "totalDeposits" still satisfies a query for "depositsTotal".</summary>
    private static decimal FindFieldValue(Dictionary<string, string> fields, string wanted)
    {
        if (fields.TryGetValue(wanted, out var exact) && TryMoney(exact, out var ev)) return ev;
        var want = Tokens(wanted);
        // Same token set (depositsTotal ≡ totalDeposits), or one is a subset of
        // the other (deposits ⊆ depositsTotal) — catches a small model's naming.
        foreach (var (k, v) in fields)
        {
            var have = Tokens(k);
            if ((want.SetEquals(have) || want.IsSubsetOf(have) || have.IsSubsetOf(want)) && TryMoney(v, out var nv))
                return nv;
        }
        return 0m;
    }

    private static bool TryMoney(string s, out decimal value) =>
        decimal.TryParse(new string((s ?? "").Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Split a field name into lower-cased word tokens, breaking on
    /// camelCase and non-alphanumerics: "depositsTotal" → {deposits, total}.</summary>
    private static HashSet<string> Tokens(string s)
    {
        var words = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var c in s ?? "")
        {
            if (char.IsUpper(c) && cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
            if (char.IsLetterOrDigit(c)) cur.Append(char.ToLowerInvariant(c));
            else if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        return [.. words.Where(w => w.Length > 0)];
    }

    private sealed record Query(string DocumentType, string Field, string Operation,
        string? SubjectContains, List<string> Periods, string Interpretation);

    private static Query? ParseQuery(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var r = doc.RootElement;
            var periods = new List<string>();
            if (r.TryGetProperty("periods", out var p) && p.ValueKind == JsonValueKind.Array)
                foreach (var e in p.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) periods.Add(e.GetString()!);
            return new Query(
                r.GetProperty("documentType").GetString() ?? "",
                r.GetProperty("field").GetString() ?? "",
                r.TryGetProperty("operation", out var o) ? o.GetString() ?? "sum" : "sum",
                r.TryGetProperty("subjectContains", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
                periods,
                r.TryGetProperty("interpretation", out var i) ? i.GetString() ?? "" : "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ReconciliationResult Fail(string error) =>
        new(false, error, "", "", "", "", 0m, 0, []);
}
