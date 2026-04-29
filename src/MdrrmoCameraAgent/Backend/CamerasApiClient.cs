using MdrrmoCameraAgent.Mtx;
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

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Fetches the camera list from the dashboard (source of truth).
    /// Called once at boot to refresh cameras.json before the publish stack starts.
    /// Per spec §7: fall back to local cache on any network failure.
    /// </summary>
    public async Task<List<CameraEntry>> ListAsync(string jwt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/agents/cameras");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var resp = await http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET /api/v1/agents/cameras failed: HTTP {(int)resp.StatusCode} — {raw}");

        using var doc = JsonDocument.Parse(raw);
        var items = doc.RootElement.GetProperty("data");
        return items.EnumerateArray()
            .Select(j => new CameraEntry(
                Id:         j.GetProperty("id").GetString()!,
                StreamPath: j.GetProperty("stream_path").GetString()!,
                RtspUrl:    j.GetProperty("rtsp_url").GetString() ?? "",
                WhipUrl:    j.TryGetProperty("whip_url", out var w) ? w.GetString() : null))
            .ToList();
    }
}
