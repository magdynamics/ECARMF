using System.Text;
using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>
/// Fully on-premise AI backend: talks to any OpenAI-compatible chat server
/// running on the operator's own machine or network — Ollama, LM Studio,
/// llama.cpp server, vLLM. No external API, no key required, nothing leaves
/// the premises. Selected per tenant via provider "local".
/// </summary>
public class LocalLanguageModelClient : ILanguageModelClient
{
    // Local models can be slow on CPU; give them room.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public LocalLanguageModelClient(string endpoint, string? model, string? apiKey = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? "llama3.1" : model;
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

    public string ModelReference => $"local:{_model}";

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseCompletion(json);
    }

    /// <summary>Extracts choices[0].message.content from an OpenAI-compatible
    /// chat completion response.</summary>
    public static string ParseCompletion(string json)
    {
        using var document = JsonDocument.Parse(json);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content ?? string.Empty;
    }
}
