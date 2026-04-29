using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

public sealed record PublishTokenResult(
    [property: JsonPropertyName("url")]        string Url,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed class PublishTokenClient(HttpClient http)
{
    public async Task<PublishTokenResult> GetAsync(string jwt, string cameraId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agents/streams/{cameraId}/publish-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PublishTokenResult>(cancellationToken: ct)
               ?? throw new InvalidOperationException("empty publish-token response");
    }
}
