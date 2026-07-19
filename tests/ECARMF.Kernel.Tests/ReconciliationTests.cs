using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Knowledge;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Reconciliation: the AI parses the request, but the PLATFORM does
/// the arithmetic over extracted document data — a total you can audit, not a
/// number the model guessed. "Add all deposits in BOA account 123", "sum
/// John's W-2 wages for the last 3 years".</summary>
public class ReconciliationTests
{
    private const string Tenant = "universal-dental";

    private readonly FakeLanguageModelClient _llm = new() { IsConfigured = true };
    private readonly InMemoryExtractedDataStore _store = new();

    private ReconciliationService Build() => new(new FakeLanguageModelProvider(_llm), _store);

    private void SeedBankStatements()
    {
        // Three BOA account-123 statements + one Chase to prove filtering.
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "boa-jan.pdf",
            DocumentType = "bank-statement", SubjectKey = "boa account 123", Period = "2026-01",
            Fields = new() { ["depositsTotal"] = "12000.50" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "boa-feb.pdf",
            DocumentType = "bank-statement", SubjectKey = "boa account 123", Period = "2026-02",
            Fields = new() { ["depositsTotal"] = "8500.25" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "boa-mar.pdf",
            DocumentType = "bank-statement", SubjectKey = "boa account 123", Period = "2026-03",
            Fields = new() { ["depositsTotal"] = "15000.00" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "chase.pdf",
            DocumentType = "bank-statement", SubjectKey = "chase account 999", Period = "2026-01",
            Fields = new() { ["depositsTotal"] = "99999.00" } }); // must NOT be included
    }

    [Fact]
    public async Task Sums_deposits_for_one_account_the_platform_does_the_math()
    {
        SeedBankStatements();
        _llm.Response = """{"documentType":"bank-statement","field":"depositsTotal","operation":"sum","subjectContains":"account 123","periods":[],"interpretation":"Total deposits for BOA account 123."}""";

        var r = await Build().RunAsync(Tenant, "review BOA account 123 and add all deposits");

        Assert.True(r.Success, r.Error);
        // 12000.50 + 8500.25 + 15000.00 = 35500.75 — Chase 99999 excluded.
        Assert.Equal(35500.75m, r.Value);
        Assert.Equal(3, r.DocumentsUsed);
        Assert.All(r.Sources, s => Assert.Contains("boa", s.FileName));
    }

    [Fact]
    public async Task Sums_W2_wages_across_named_years_only()
    {
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "john-2023.pdf",
            DocumentType = "w2", SubjectKey = "john smith", Period = "2023", Fields = new() { ["wages"] = "62000" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "john-2024.pdf",
            DocumentType = "w2", SubjectKey = "john smith", Period = "2024", Fields = new() { ["wages"] = "65000" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "john-2025.pdf",
            DocumentType = "w2", SubjectKey = "john smith", Period = "2025", Fields = new() { ["wages"] = "68000" } });
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "john-2019.pdf",
            DocumentType = "w2", SubjectKey = "john smith", Period = "2019", Fields = new() { ["wages"] = "40000" } }); // out of range
        _llm.Response = """{"documentType":"w2","field":"wages","operation":"sum","subjectContains":"john","periods":["2023","2024","2025"],"interpretation":"John's W-2 wages for 2023-2025."}""";

        var r = await Build().RunAsync(Tenant, "review employee John and add all of his W2 wages for the last 3 years");

        Assert.True(r.Success, r.Error);
        Assert.Equal(195000m, r.Value); // 62000+65000+68000, the 2019 one excluded
        Assert.Equal(3, r.DocumentsUsed);
    }

    [Fact]
    public async Task Tolerates_a_models_field_name_variants()
    {
        // The extractor stored "totalDeposits"; the query field is "depositsTotal".
        _store.Items.Add(new ExtractedDocumentData { TenantId = Tenant, DocumentId = Guid.NewGuid(), FileName = "boa.pdf",
            DocumentType = "bank-statement", SubjectKey = "boa 123", Period = "2026-01",
            Fields = new() { ["totalDeposits"] = "$1,200.00" } }); // reordered name + formatting
        _llm.Response = """{"documentType":"bank-statement","field":"depositsTotal","operation":"sum","subjectContains":"123","periods":[],"interpretation":"x"}""";

        var r = await Build().RunAsync(Tenant, "add deposits for 123");
        Assert.Equal(1200m, r.Value); // matched despite name reorder and $/comma
    }

    [Fact]
    public async Task Rejects_a_non_aggregatable_field()
    {
        _llm.Response = """{"documentType":"w2","field":"employeeName","operation":"sum","subjectContains":null,"periods":[],"interpretation":"x"}""";
        var r = await Build().RunAsync(Tenant, "sum employee names");
        Assert.False(r.Success);
        Assert.Contains("not an aggregatable field", r.Error);
    }

    [Fact]
    public async Task Counts_documents_when_asked()
    {
        SeedBankStatements();
        _llm.Response = """{"documentType":"bank-statement","field":"depositsTotal","operation":"count","subjectContains":"account 123","periods":[],"interpretation":"How many BOA 123 statements."}""";
        var r = await Build().RunAsync(Tenant, "how many statements for BOA account 123");
        Assert.Equal("count", r.Operation);
        Assert.Equal(3m, r.Value);
    }
}
