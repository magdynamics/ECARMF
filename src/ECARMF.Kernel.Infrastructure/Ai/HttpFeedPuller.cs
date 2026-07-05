using System.Net.Http.Headers;
using ECARMF.Kernel.Application.Integrations;

namespace ECARMF.Kernel.Infrastructure.Ai;

/// <summary>HTTP transport for pull-mode integrations: GET the application's
/// export endpoint, optionally with a bearer secret. OAuth exchanges or
/// vendor SDKs replace this class later without touching the feed service.</summary>
public class HttpFeedPuller : IFeedPuller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpFeedPuller(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    public async Task<string> FetchAsync(string url, string? bearerSecret, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("integration-feeds");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(bearerSecret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerSecret);
        }

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
