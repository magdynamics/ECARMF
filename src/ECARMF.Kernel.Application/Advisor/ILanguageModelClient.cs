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

/// <summary>Default backend when nothing is configured.</summary>
public sealed class UnconfiguredLanguageModelClient : ILanguageModelClient
{
    public bool IsConfigured => false;

    public string ModelReference => "unconfigured";

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("No language model backend is configured.");
}
