using ECARMF.Kernel.Api.Endpoints;
using ECARMF.Kernel.Api.Hosting;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// The operator "act as tenant" path: a platform-operator access key may
/// target another tenant via X-Act-As-Tenant (so one credential runs the
/// whole console), while a client key can never cross its own boundary.
/// </summary>
public class ActAsTenantMiddlewareTests
{
    private const string OperatorKey = "ecarmf_operator";
    private const string ClientKey = "ecarmf_client";

    private static ApiKeyAuthenticationMiddleware Build(out FakeTenantDirectory tenants, out KeyUserStore users)
    {
        tenants = new FakeTenantDirectory();
        tenants.Profiles["platform"] = new TenantProfile { TenantId = "platform", Name = "Platform", Status = "Active" };
        tenants.Profiles["jj-fish"] = new TenantProfile { TenantId = "jj-fish", Name = "JJ Fish", Status = "Active" };

        users = new KeyUserStore();
        users.ByKeyHash[AccessKey.Hash(OperatorKey)] = new User
        {
            TenantId = "platform", Identifier = SeedUsers.Owner, Status = "Active",
            Roles = new List<string> { RoleCatalog.ExecutiveOwner }
        };
        users.ByKeyHash[AccessKey.Hash(ClientKey)] = new User
        {
            TenantId = "jj-fish", Identifier = SeedUsers.Owner, Status = "Active",
            Roles = new List<string> { RoleCatalog.ExecutiveOwner }
        };

        var config = new ConfigurationBuilder().Build();
        return new ApiKeyAuthenticationMiddleware(_ => Task.CompletedTask, config);
    }

    private static DefaultHttpContext Request(string apiKey, string? actAs, string path = "/api/packages")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Headers[ApiKeyAuthenticationMiddleware.ApiKeyHeader] = apiKey;
        if (actAs is not null)
            ctx.Request.Headers[ApiKeyAuthenticationMiddleware.ActAsTenantHeader] = actAs;
        return ctx;
    }

    [Fact]
    public async Task Operator_key_can_act_as_another_tenant()
    {
        var mw = Build(out var tenants, out var users);
        var ctx = Request(OperatorKey, actAs: "jj-fish");

        await mw.InvokeAsync(ctx, users, tenants);

        // Tenant becomes the target; identity stays the operator (seeded in
        // every tenant, so downstream permission checks resolve there).
        Assert.Equal("jj-fish", ctx.Request.Headers[TenantResolution.HeaderName]);
        Assert.Equal(SeedUsers.Owner, ctx.Request.Headers[AccessGuard.UserHeader]);
        Assert.Equal("jj-fish", ctx.Items["ActingAsTenant"]);
    }

    [Fact]
    public async Task Client_key_cannot_act_as_another_tenant()
    {
        var mw = Build(out var tenants, out var users);
        var ctx = Request(ClientKey, actAs: "magcpa");

        await mw.InvokeAsync(ctx, users, tenants);

        // The header is ignored — a client key stays pinned to its own tenant.
        Assert.Equal("jj-fish", ctx.Request.Headers[TenantResolution.HeaderName]);
        Assert.False(ctx.Items.ContainsKey("ActingAsTenant"));
    }

    [Fact]
    public async Task Operator_key_without_act_as_stays_on_platform()
    {
        var mw = Build(out var tenants, out var users);
        var ctx = Request(OperatorKey, actAs: null);

        await mw.InvokeAsync(ctx, users, tenants);

        Assert.Equal("platform", ctx.Request.Headers[TenantResolution.HeaderName]);
    }

    private sealed class KeyUserStore : IUserStore
    {
        public Dictionary<string, User> ByKeyHash { get; } = new();

        public Task<User?> GetByAccessKeyHashAsync(string accessKeyHash, CancellationToken ct = default) =>
            Task.FromResult(ByKeyHash.TryGetValue(accessKeyHash, out var u) ? u : null);

        public Task<User?> GetByIdentifierAsync(string tenantId, string identifier, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<User>>(new List<User>());
        public Task EnsureSeedUsersAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateUserAsync(User user, string? accessKeyHash, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetAccessKeyHashAsync(string tenantId, string identifier, string accessKeyHash, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetStatusAsync(string tenantId, string identifier, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRolesAsync(string tenantId, string identifier, IReadOnlyList<string> roles, CancellationToken ct = default) => Task.CompletedTask;
    }
}
