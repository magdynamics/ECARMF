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
/// Resolves the AI backend per tenant. The tenant's own stored configuration
/// wins — either a fully on-premise local model server (provider "local":
/// Ollama / LM Studio / llama.cpp — no external key, nothing leaves the
/// machine) or the tenant's own Anthropic credential. Platform-level
/// fallbacks come from configuration (LocalAi:Endpoint for an on-prem
/// default, or Anthropic:ApiKey / ANTHROPIC_API_KEY for development). With
/// nothing configured, agents use their deterministic composers.
/// </summary>
public class TenantLanguageModelProvider : ILanguageModelProvider
{
    private static readonly UnconfiguredLanguageModelClient Unconfigured = new();

    private readonly ITenantAiSettingsStore _settings;
    private readonly string? _platformLocalEndpoint;
    private readonly string? _platformLocalModel;
    private readonly string? _platformApiKey;
    private readonly string? _platformModel;

    public TenantLanguageModelProvider(ITenantAiSettingsStore settings, IConfiguration configuration)
    {
        _settings = settings;
        _platformLocalEndpoint = configuration["LocalAi:Endpoint"];
        _platformLocalModel = configuration["LocalAi:Model"];
        _platformApiKey = configuration["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _platformModel = configuration["Anthropic:Model"];
    }

    public async Task<ILanguageModelClient> GetForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = await _settings.GetCredentialsAsync(tenantId, ct);
        if (tenant is not null)
        {
            if (string.Equals(tenant.Provider, AiProviders.Local, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(tenant.Endpoint))
            {
                return new LocalLanguageModelClient(tenant.Endpoint, tenant.Model, tenant.ApiKey);
            }

            if (!string.IsNullOrWhiteSpace(tenant.ApiKey))
            {
                return new AnthropicLanguageModelClient(tenant.ApiKey, tenant.Model);
            }
        }

        // Platform-level defaults: prefer the fully independent on-prem server.
        if (!string.IsNullOrWhiteSpace(_platformLocalEndpoint))
        {
            return new LocalLanguageModelClient(_platformLocalEndpoint, _platformLocalModel);
        }

        if (!string.IsNullOrWhiteSpace(_platformApiKey))
        {
            return new AnthropicLanguageModelClient(_platformApiKey, _platformModel);
        }

        return Unconfigured;
    }
}
