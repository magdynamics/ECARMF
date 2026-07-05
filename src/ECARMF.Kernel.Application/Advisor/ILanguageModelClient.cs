namespace ECARMF.Kernel.Application.Advisor;

/// <summary>
/// Pluggable reasoning backend for AI agents. The kernel depends only on
/// this port: the shipped implementation calls the Anthropic API when an
/// API key is configured, and agents fall back to their deterministic
/// composers when it is not — the mechanism works either way, and a smarter
/// backend never requires kernel changes.
/// </summary>
public interface ILanguageModelClient
{
    /// <summary>False when no API key is configured; callers must use their
    /// deterministic fallback instead of calling <see cref="CompleteAsync"/>.</summary>
    bool IsConfigured { get; }

    /// <summary>Identifier of the underlying model (e.g. "claude-opus-4-8")
    /// for provenance and ModelAccuracy tracking.</summary>
    string ModelReference { get; }

    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>
/// Tenant-scoped resolution of the language model backend. The platform
/// serves multiple clients: every tenant configures its own AI credential
/// (and model), so usage, billing, and trust never cross tenants. A tenant
/// without a credential gets an unconfigured client and agents use their
/// deterministic fallbacks.
/// </summary>
public interface ILanguageModelProvider
{
    Task<ILanguageModelClient> GetForTenantAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>Supported AI backend providers. "local" is any OpenAI-compatible
/// server on the operator's own machine (Ollama, LM Studio, llama.cpp,
/// vLLM) — fully independent, no external key, nothing leaves the premises.</summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string Local = "local";
}

/// <summary>A tenant's resolved AI configuration for the runtime provider.</summary>
public sealed record TenantAiCredentials(string Provider, string? ApiKey, string? Endpoint, string? Model);

/// <summary>Masked, UI-safe view of a tenant's AI settings — the key itself
/// is write-only and never leaves the store unprotected.</summary>
public sealed record TenantAiSettingsStatus(
    bool Configured,
    string? Provider,
    string? Endpoint,
    string? Model,
    string? ApiKeyHint,
    string? ConfiguredBy,
    DateTimeOffset? UpdatedAt);

/// <summary>Tenant-scoped AI configuration store. Keys are stored protected
/// at rest and are never returned by status reads.</summary>
public interface ITenantAiSettingsStore
{
    /// <summary>Unprotected configuration for the runtime provider only.</summary>
    Task<TenantAiCredentials?> GetCredentialsAsync(string tenantId, CancellationToken ct = default);

    Task<TenantAiSettingsStatus> GetStatusAsync(string tenantId, CancellationToken ct = default);

    Task SetAsync(
        string tenantId, string provider, string? apiKey, string? endpoint, string? model,
        string configuredBy, CancellationToken ct = default);

    Task ClearAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>Default backend when nothing is configured.</summary>
public sealed class UnconfiguredLanguageModelClient : ILanguageModelClient
{
    public bool IsConfigured => false;

    public string ModelReference => "unconfigured";

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("No language model backend is configured for this tenant.");
}
