using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using ECARMF.Kernel.Application.Advisor;
using Microsoft.Extensions.Configuration;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// Anthropic-backed implementation of the language model port. Activated by
/// configuration ("Anthropic:ApiKey" or the ANTHROPIC_API_KEY environment
/// variable); when no key is present, IsConfigured is false and agents use
/// their deterministic composers instead.
/// </summary>
public class AnthropicLanguageModelClient : ILanguageModelClient
{
    public const string DefaultModel = "claude-opus-4-8";

    private readonly string? _apiKey;
    private readonly string _model;

    public AnthropicLanguageModelClient(IConfiguration configuration)
    {
        _apiKey = configuration["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _model = configuration["Anthropic:Model"] ?? DefaultModel;
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
