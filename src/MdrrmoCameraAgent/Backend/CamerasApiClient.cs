using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MdrrmoCameraAgent.Backend;

public sealed class CamerasApiClient(HttpClient http)
{
    public async Task<JsonElement> InsertCameraAsync(string jwt, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/cameras/")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var resp = await http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"cameras endpoint failed: HTTP {(int)resp.StatusCode} — {raw}");

        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}
