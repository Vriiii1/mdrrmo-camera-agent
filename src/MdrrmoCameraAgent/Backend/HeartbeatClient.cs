using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

public sealed record HeartbeatCamera(string Id, string Status, DateTimeOffset? LastFrameAt);

public sealed record HeartbeatResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("rejected")] int Rejected);

public sealed class HeartbeatClient(HttpClient http)
{
    public async Task<HeartbeatResult> SendAsync(
        string jwt, IEnumerable<HeartbeatCamera> cameras, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                cameras = cameras.Select(c => new
                {
                    id            = c.Id,
                    status        = c.Status,
                    last_frame_at = c.LastFrameAt?.UtcDateTime.ToString("o")
                })
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HeartbeatResult>(cancellationToken: ct)
               ?? throw new InvalidOperationException("empty heartbeat response");
    }
}
