using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Knowledge;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryDocumentAllocationStore : IDocumentAllocationStore
{
    public List<DocumentAllocation> Items { get; } = [];
    public Task AddAsync(DocumentAllocation a, CancellationToken ct = default) { Items.Add(a); return Task.CompletedTask; }
    public Task<DocumentAllocation?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.TenantId == tenantId && a.Id == id));
    public Task<IReadOnlyList<DocumentAllocation>> GetByStatusAsync(string tenantId, string? status, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DocumentAllocation>>(
            Items.Where(a => a.TenantId == tenantId && (status is null || a.Status == status)).ToList());
    public Task UpdateAsync(DocumentAllocation a, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Bulk mixed-document triage: the AI recommends which business unit
/// each document belongs to; a human confirms; only then is it filed with the
/// unit stamped. AI recommends, humans decide.</summary>
public class DocumentTriageTests
{
    private const string Tenant = "universal-dental";

    private readonly InMemoryOrgUnitStore _units = new();
    private readonly FakeLanguageModelClient _llm = new() { IsConfigured = true };
    private readonly InMemoryDocumentLibrary _library = new();
    private readonly InMemoryDocumentAllocationStore _allocations = new();
    private readonly InMemoryAuditLog _audit = new();

    private DocumentTriageService Build()
    {
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "oak-lawn", Name = "Oak Lawn", UnitType = "LegalEntity", Industry = "dental" });
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "pulaski", Name = "Pulaski", UnitType = "LegalEntity", Industry = "dental" });
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "ahmad-ramaha", Name = "Ahmad Ramaha", UnitType = "Principal" });
        return new DocumentTriageService(_units, new FakeLanguageModelProvider(_llm), _library, _allocations, _audit);
    }

    [Fact]
    public async Task Analyze_recommends_a_unit_archives_pending_and_queues_for_review()
    {
        var service = Build();
        _llm.Response = """{"unitRef":"oak-lawn","documentType":"Invoice","confidence":0.88,"reasoning":"Vendor invoice addressed to the Oak Lawn clinic."}""";

        var outcome = await service.AnalyzeAsync(Tenant, "invoice-4471.pdf", "INVOICE — Oak Lawn Dental, 5200 W...", null, "admin@platform");

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("oak-lawn", outcome.Allocation!.RecommendedUnitRef);
        Assert.Equal("Invoice", outcome.Allocation.DocumentType);
        Assert.Equal(0.88m, outcome.Allocation.Confidence);
        Assert.Equal(DocumentAllocationStatuses.Pending, outcome.Allocation.Status);
        // Archived, but NOT yet filed to a unit — a human decides first.
        var archived = _library.Items.Single().Document;
        Assert.Equal("triage-pending", archived.SourceCategory);
        Assert.Null(archived.UnitRef);
    }

    [Fact]
    public async Task A_hallucinated_unit_is_dropped_not_recommended()
    {
        var service = Build();
        _llm.Response = """{"unitRef":"nowhere-clinic","documentType":"Lease","confidence":0.9,"reasoning":"x"}""";

        var outcome = await service.AnalyzeAsync(Tenant, "lease.pdf", "LEASE...", null, "admin@platform");

        Assert.Null(outcome.Allocation!.RecommendedUnitRef); // invalid unit dropped
    }

    [Fact]
    public async Task Confirming_files_the_document_with_the_unit_stamped()
    {
        var service = Build();
        _llm.Response = """{"unitRef":"pulaski","documentType":"License","confidence":0.8,"reasoning":"State dental license for Pulaski."}""";
        var analyzed = await service.AnalyzeAsync(Tenant, "license.pdf", "STATE OF ILLINOIS DENTAL LICENSE...", null, "admin@platform");

        var decider = new User { TenantId = Tenant, Identifier = "reviewer@ud", DisplayName = "Reviewer" };
        var decided = await service.DecideAsync(Tenant, analyzed.Allocation!.Id, "pulaski", decider);

        Assert.True(decided.Success, decided.Error);
        Assert.Equal(DocumentAllocationStatuses.Confirmed, decided.Allocation!.Status); // matched the AI
        var filed = _library.Items.Single().Document;
        Assert.Equal("pulaski", filed.UnitRef);       // the document is now the unit's
        Assert.Equal("triage-filed", filed.SourceCategory);
    }

    [Fact]
    public async Task Reassigning_to_a_different_unit_is_marked_reassigned()
    {
        var service = Build();
        _llm.Response = """{"unitRef":"oak-lawn","documentType":"Invoice","confidence":0.6,"reasoning":"Ambiguous."}""";
        var analyzed = await service.AnalyzeAsync(Tenant, "doc.pdf", "text", null, "admin@platform");

        var decider = new User { TenantId = Tenant, Identifier = "reviewer@ud", DisplayName = "Reviewer" };
        // Human overrides to the owner entity.
        var decided = await service.DecideAsync(Tenant, analyzed.Allocation!.Id, "ahmad-ramaha", decider);

        Assert.Equal(DocumentAllocationStatuses.Reassigned, decided.Allocation!.Status);
        Assert.Equal("ahmad-ramaha", _library.Items.Single().Document.UnitRef);
    }

    [Fact]
    public async Task Decide_rejects_an_unknown_unit()
    {
        var service = Build();
        _llm.Response = """{"unitRef":"oak-lawn","documentType":"x","confidence":0.5,"reasoning":"y"}""";
        var analyzed = await service.AnalyzeAsync(Tenant, "d.pdf", "t", null, "admin@platform");

        var decider = new User { TenantId = Tenant, Identifier = "r@ud", DisplayName = "R" };
        var decided = await service.DecideAsync(Tenant, analyzed.Allocation!.Id, "ghost-unit", decider);

        Assert.False(decided.Success);
        Assert.Contains("does not exist", decided.Error);
    }
}
