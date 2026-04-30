using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

public sealed record HeartbeatCamera(
    [property: JsonPropertyName("id")]             string Id,
    [property: JsonPropertyName("status")]         string Status,
    [property: JsonPropertyName("last_frame_at")]  DateTimeOffset? LastFrameAt,
    [property: JsonPropertyName("publish_status")] string? PublishStatus = null);

public sealed record HeartbeatResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("rejected")] int Rejected);

public sealed class HeartbeatException(string msg) : Exception(msg);

public sealed class HeartbeatClient(HttpClient http)
{
    public async Task<HeartbeatResult> SendAsync(
        string jwt, IEnumerable<HeartbeatCamera> cameras, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/heartbeat/")
        {
            Content = JsonContent.Create(new
            {
                cameras = cameras.Select(c =>
                {
                    var dict = new Dictionary<string, object?>
                    {
                        ["id"]             = c.Id,
                        ["status"]         = c.Status,
                        ["last_frame_at"]  = c.LastFrameAt?.ToString("o"),
                    };
                    if (c.PublishStatus is not null) dict["publish_status"] = c.PublishStatus;
                    return dict;
                })
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Console.WriteLine($"[hb-diag] uri={http.BaseAddress}{req.RequestUri} authHeaderSet={req.Headers.Authorization is not null} authScheme={req.Headers.Authorization?.Scheme}");

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HeartbeatException($"heartbeat failed: HTTP {(int)resp.StatusCode} — {body}");
        }
        return await resp.Content.ReadFromJsonAsync<HeartbeatResult>(cancellationToken: ct)
               ?? throw new HeartbeatException("empty heartbeat response");
    }
}
