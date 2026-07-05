using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

namespace ECARMF.Kernel.Tests;

public class FakeAiSettingsStore : ITenantAiSettingsStore
{
    public Dictionary<string, TenantAiCredentials> Items { get; } = [];

    public Task<TenantAiCredentials?> GetCredentialsAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(tenantId, out var c) ? c : null);
    public Task<TenantAiSettingsStatus> GetStatusAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(new TenantAiSettingsStatus(false, null, null, null, null, null, null));
    public Task SetAsync(string tenantId, string provider, string? apiKey, string? endpoint, string? model,
        string configuredBy, CancellationToken ct = default) => Task.CompletedTask;
    public Task ClearAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Fully on-prem AI: the provider resolves each tenant to its own
/// backend — a local OpenAI-compatible server (no external key, nothing
/// leaves the machine), an Anthropic credential, or deterministic fallback.</summary>
public class LocalAiBackendTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    [Fact]
    public async Task Tenant_configured_local_server_wins_and_needs_no_key()
    {
        var store = new FakeAiSettingsStore();
        store.Items["t1"] = new TenantAiCredentials("local", null, "http://localhost:11434", "llama3.1");
        var provider = new TenantLanguageModelProvider(store, Config());

        var client = await provider.GetForTenantAsync("t1");

        Assert.True(client.IsConfigured);
        Assert.Equal("local:llama3.1", client.ModelReference);
        Assert.IsType<LocalLanguageModelClient>(client);
    }

    [Fact]
    public async Task Platform_local_endpoint_is_preferred_over_external_fallback()
    {
        var provider = new TenantLanguageModelProvider(new FakeAiSettingsStore(), Config(
            ("LocalAi:Endpoint", "http://localhost:11434"),
            ("LocalAi:Model", "mistral"),
            ("Anthropic:ApiKey", "sk-ant-should-not-be-used")));

        var client = await provider.GetForTenantAsync("t1");

        Assert.IsType<LocalLanguageModelClient>(client);
        Assert.Equal("local:mistral", client.ModelReference);
    }

    [Fact]
    public async Task Tenant_anthropic_credential_still_works_and_isolation_holds()
    {
        var store = new FakeAiSettingsStore();
        store.Items["t1"] = new TenantAiCredentials("anthropic", "sk-ant-tenant1", null, "claude-opus-4-8");
        var provider = new TenantLanguageModelProvider(store, Config());

        var t1 = await provider.GetForTenantAsync("t1");
        var t2 = await provider.GetForTenantAsync("t2"); // nothing configured anywhere

        Assert.IsType<AnthropicLanguageModelClient>(t1);
        Assert.False(t2.IsConfigured); // one tenant's credential never leaks to another
    }

    [Fact]
    public void OpenAi_compatible_response_parses_to_the_message_content()
    {
        const string response = """
            {"id":"chatcmpl-1","object":"chat.completion","model":"llama3.1",
             "choices":[{"index":0,"message":{"role":"assistant","content":"The return is a pass-through."},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":10,"completion_tokens":8}}
            """;

        Assert.Equal("The return is a pass-through.", LocalLanguageModelClient.ParseCompletion(response));
    }
}
