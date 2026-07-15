using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryFundingSourceStore : IFundingSourceStore
{
    public List<FundingSource> Items { get; } = [];
    public Task<FundingSource?> GetAsync(string tenantId, string sourceId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.TenantId == tenantId
            && string.Equals(s.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<FundingSource>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FundingSource>>(Items.Where(s => s.TenantId == tenantId).ToList());
    public Task AddAsync(FundingSource source, CancellationToken ct = default)
    { Items.Add(source); return Task.CompletedTask; }
    public Task UpdateAsync(FundingSource source, CancellationToken ct = default) => Task.CompletedTask;
}

public class InMemoryFundingEventStore : IFundingEventStore
{
    public List<FundingEvent> Items { get; } = [];
    public Task<FundingEvent?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(e => e.TenantId == tenantId && e.Id == id));
    public Task<IReadOnlyList<FundingEvent>> GetBySourceAsync(string tenantId, Guid fundingSourceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FundingEvent>>(
            Items.Where(e => e.TenantId == tenantId && e.FundingSourceId == fundingSourceId).ToList());
    public Task AddAsync(FundingEvent fundingEvent, CancellationToken ct = default)
    { Items.Add(fundingEvent); return Task.CompletedTask; }
    public Task UpdateAsync(FundingEvent fundingEvent, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Rosetta kernel extensions: lifecycle-aware framework attachment
/// on Project units, inbound FundingSource capital (debt + equity, one
/// mechanism), and milestone-gated obligations.</summary>
public class RosettaKernelTests
{
    private const string Tenant = "rosetta";

    // ---- Lifecycle-aware framework attachment (Requirement 3) ----------

    private sealed class StubPackageStore : ECARMF.Kernel.Application.Packages.IPackageStore
    {
        private readonly string[] _active;
        public StubPackageStore(params string[] active) => _active = active;

        public Task<bool> ExistsAsync(string t, string p, string v, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddAsync(string t, Domain.Packages.KnowledgePackageManifest m, Domain.Packages.PackageLoadState s, string? d, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateStateAsync(string t, string p, string v, Domain.Packages.PackageLoadState s, string? d, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ECARMF.Kernel.Application.Packages.StoredPackage?> GetAsync(string t, string p, string v, CancellationToken ct = default) =>
            Task.FromResult<ECARMF.Kernel.Application.Packages.StoredPackage?>(null);
        public Task<IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage>> GetAllAsync(string t, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage>>([]);
        public Task<IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage>> GetByStateAsync(
            string t, Domain.Packages.PackageLoadState s, CancellationToken ct = default) =>
            Task.FromResult(Active(t));
        public Task<IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage>> GetByStateAllTenantsAsync(
            Domain.Packages.PackageLoadState s, CancellationToken ct = default) =>
            Task.FromResult(Active(Tenant));
        public Task<IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage>> GetAllAcrossTenantsAsync(
            CancellationToken ct = default) =>
            Task.FromResult(Active(Tenant));

        private IReadOnlyList<ECARMF.Kernel.Application.Packages.StoredPackage> Active(string tenantId) =>
            _active.Select(id => new ECARMF.Kernel.Application.Packages.StoredPackage(
                tenantId,
                new Domain.Packages.KnowledgePackageManifest { PackageId = id, PackageVersion = "1.0.0" },
                Domain.Packages.PackageLoadState.Active, null)).ToList();
    }

    private sealed class InMemoryOrgUnitStore : IOrgUnitStore
    {
        public List<OrganizationalUnit> Items { get; } = [];
        public Task<OrganizationalUnit?> GetAsync(string tenantId, string unitId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(u => u.TenantId == tenantId
                && string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase)));
        public Task<IReadOnlyList<OrganizationalUnit>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationalUnit>>(Items.Where(u => u.TenantId == tenantId).ToList());
        public Task AddAsync(OrganizationalUnit unit, CancellationToken ct = default)
        { Items.Add(unit); return Task.CompletedTask; }
        public Task UpdateAsync(OrganizationalUnit unit, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default)
        { Items.RemoveAll(u => u.TenantId == tenantId && u.UnitId == unitId); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Framework_packages_follow_the_lifecycle_state_automatically()
    {
        var units = new InMemoryOrgUnitStore();
        var audit = new InMemoryAuditLog();
        var notifications = new InMemoryNotificationStore();
        var service = new OrgUnitService(
            units, new StubPackageStore("ecarmf.ai-construction", "ecarmf.realestate-asset"),
            audit, notifications);

        var project = new OrganizationalUnit
        {
            TenantId = Tenant, UnitId = "burbank-hotel", Name = "Burbank Hotel Development",
            UnitType = "Project", LifecycleState = "Construction",
            AttachedPackageIds = ["ecarmf.ai-construction", "ecarmf.connector-reference-templates"],
            LifecyclePackageMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Construction"] = ["ecarmf.ai-construction"],
                ["OperatingAsset"] = ["ecarmf.realestate-asset"]
            }
        };
        units.Items.Add(project);

        var updated = await service.SetLifecycleStateAsync(Tenant, "burbank-hotel", "OperatingAsset", "owner");

        // The construction package detached, the real-estate package attached...
        Assert.Contains("ecarmf.realestate-asset", updated.AttachedPackageIds);
        Assert.DoesNotContain("ecarmf.ai-construction", updated.AttachedPackageIds);
        // ...and the hand-attached package (not in the map) was untouched.
        Assert.Contains("ecarmf.connector-reference-templates", updated.AttachedPackageIds);
        Assert.Contains(audit.Items, a => a.Category == AuditCategories.OrgUnitPackagesChanged
            && a.Summary.Contains("Lifecycle-driven"));
        Assert.Contains(notifications.Items, n => n.Message.Contains("Lifecycle map applied automatically"));
    }

    [Fact]
    public async Task Lifecycle_map_rejects_packages_that_are_not_active()
    {
        var units = new InMemoryOrgUnitStore();
        var service = new OrgUnitService(
            units, new StubPackageStore("ecarmf.ai-construction"), new InMemoryAuditLog());
        units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "burbank-hotel", Name = "Burbank" });

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetLifecyclePackageMapAsync(
            Tenant, "burbank-hotel",
            new Dictionary<string, List<string>> { ["Construction"] = ["ecarmf.not-activated"] }, "owner"));
    }

    // ---- FundingSource (Requirement 4) ---------------------------------

    [Fact]
    public async Task Lender_draw_flows_request_decision_disbursement_with_full_audit()
    {
        var sources = new InMemoryFundingSourceStore();
        var events = new InMemoryFundingEventStore();
        var audit = new InMemoryAuditLog();
        var notifications = new InMemoryNotificationStore();
        var service = new FundingService(sources, events, audit, notifications);

        await service.CreateSourceAsync(new FundingSource
        {
            TenantId = Tenant, SourceId = "construction-lender", UnitId = "burbank-hotel",
            Kind = FundingSourceKinds.Debt, Name = "Construction Lender (TBD)", CommitmentAmount = 24000000m
        }, "admin");

        var draw = await service.RequestEventAsync(Tenant, "construction-lender", new FundingEvent
        {
            EventType = FundingEventTypes.Draw, Amount = 1800000m,
            MilestoneReference = "foundation-complete", PercentCompleteClaimed = 1.0m,
            VerificationNote = "SiteView completion events corroborate foundation phase closure."
        }, "pm@rosetta");

        Assert.Equal(FundingEventStatuses.Requested, draw.Status);
        Assert.Contains(audit.Items, a => a.Category == AuditCategories.FundingEventRequested);
        Assert.Contains(notifications.Items, n =>
            n.Target == "TreasuryOfficer" && n.Message.Contains("foundation-complete"));

        var human = new User { Identifier = "owner@rosetta", IsSystemActor = false };
        var approved = await service.DecideAsync(Tenant, draw.Id, human, true, "Verified against SiteView.");
        Assert.Equal(FundingEventStatuses.Approved, approved.Status);

        var disbursed = await service.MarkDisbursedAsync(Tenant, draw.Id, "treasury@rosetta");
        Assert.Equal(FundingEventStatuses.Disbursed, disbursed.Status);
        Assert.Contains(audit.Items, a => a.Category == AuditCategories.FundingEventDisbursed);
    }

    [Fact]
    public async Task Funding_guards_hold_ai_cannot_decide_and_draws_need_milestones()
    {
        var sources = new InMemoryFundingSourceStore();
        var events = new InMemoryFundingEventStore();
        var service = new FundingService(
            sources, events, new InMemoryAuditLog(), new InMemoryNotificationStore());
        await service.CreateSourceAsync(new FundingSource
        {
            TenantId = Tenant, SourceId = "investor-alpha", UnitId = "burbank-hotel",
            Kind = FundingSourceKinds.Equity, Name = "Investor (TBD)"
        }, "admin");

        // A lender-style draw with no milestone is rejected.
        await Assert.ThrowsAsync<ArgumentException>(() => service.RequestEventAsync(
            Tenant, "investor-alpha",
            new FundingEvent { EventType = FundingEventTypes.Draw, Amount = 100m }, "pm"));

        // A capital call needs no milestone (equity shape)...
        var call = await service.RequestEventAsync(Tenant, "investor-alpha",
            new FundingEvent { EventType = FundingEventTypes.CapitalCall, Amount = 500000m }, "pm");

        // ...but an AI actor can never decide capital movement.
        var ai = new User { Identifier = "system:flywheel", IsSystemActor = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideAsync(Tenant, call.Id, ai, true, null));
    }

    // ---- Milestone-gated obligations (Requirement 5) --------------------

    [Fact]
    public async Task Milestone_gated_obligations_stay_dormant_until_the_gate_opens()
    {
        var renewals = new InMemoryRenewalStore();
        var alerts = new InMemoryDeviationStore();
        var monitor = new RenewalMonitorService(
            renewals, alerts, new InMemoryNotificationStore(), new InMemoryTaskStore(), new InMemoryAuditLog());

        var occupancy = new RenewalCommitment
        {
            TenantId = Tenant,
            Name = "Certificate of occupancy - Burbank hotel",
            Category = "Permit",
            SubjectType = "unit",
            SubjectId = "burbank-hotel",
            DueDate = DateTimeOffset.UtcNow.AddDays(-30), // long "overdue" by the calendar...
            RecurrenceMonths = null,
            MilestoneReference = "construction-complete", // ...but gated
            NotifyRole = "ExecutiveOwner",
            CreatedBy = "admin"
        };
        renewals.Items.Add(occupancy);

        // Dormant: the monitor raises NOTHING while the milestone is open.
        Assert.Equal(0, await monitor.EvaluateAsync(Tenant, DateTimeOffset.UtcNow));
        Assert.Empty(alerts.Items);

        // The gate opens; the same obligation is now live and escalates.
        occupancy.MilestoneReachedAt = DateTimeOffset.UtcNow;
        occupancy.DueDate = DateTimeOffset.UtcNow.AddDays(5);
        Assert.True(await monitor.EvaluateAsync(Tenant, DateTimeOffset.UtcNow) > 0);
        Assert.NotEmpty(alerts.Items);
    }
}
