using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using ECARMF.Kernel.Application.Advisor;
using Microsoft.Extensions.Configuration;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// Anthropic-backed implementation of the language model port, constructed
/// per tenant with that tenant's own credential and model choice.
/// </summary>
public class AnthropicLanguageModelClient : ILanguageModelClient
{
    public const string DefaultModel = "claude-opus-4-8";

    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicLanguageModelClient(string apiKey, string? model = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public string ModelReference => _model;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Anthropic API key is not configured.");
        }

        var client = new AnthropicClient { ApiKey = _apiKey };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 8000,
            System = systemPrompt,
            Thinking = new ThinkingConfigAdaptive(),
            Messages = [new MessageParam { Role = "user", Content = userPrompt }]
        }, ct);

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text.Append(textBlock.Text);
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// Resolves the AI backend per tenant: the tenant's own stored credential
/// wins; the platform-operator credential (Anthropic:ApiKey configuration or
/// ANTHROPIC_API_KEY) is only a development/demo fallback; with neither, the
/// unconfigured client makes agents use their deterministic composers.
/// </summary>
public class AnthropicLanguageModelProvider : ILanguageModelProvider
{
    private static readonly UnconfiguredLanguageModelClient Unconfigured = new();

    private readonly ITenantAiSettingsStore _settings;
    private readonly string? _platformApiKey;
    private readonly string? _platformModel;

    public AnthropicLanguageModelProvider(ITenantAiSettingsStore settings, IConfiguration configuration)
    {
        _settings = settings;
        _platformApiKey = configuration["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _platformModel = configuration["Anthropic:Model"];
    }

    public async Task<ILanguageModelClient> GetForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var (apiKey, model) = await _settings.GetCredentialsAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return new AnthropicLanguageModelClient(apiKey, model);
        }

        if (!string.IsNullOrWhiteSpace(_platformApiKey))
        {
            return new AnthropicLanguageModelClient(_platformApiKey, _platformModel);
        }

        return Unconfigured;
    }
}
