using System.Text.Json;

namespace MdrrmoCameraAgent.Mtx;

/// <summary>
/// Polls MediaMTX's local /v3/paths/list and computes per-stream
/// publish_status from a state machine over (bytesReceived Δ, bytesSent Δ,
/// readers count). bytesReceived alone is insufficient — it only measures
/// the camera→mediamtx leg, so a hub outage is invisible to it. See plan
/// table above Step 1 for the full truth table.
///
/// Caller (`PublishLoopHost`) drives this on a 30s tick.
/// </summary>
public sealed class PublishStatusProbe
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, (long BytesReceived, long BytesSent)> _last = new();

    public PublishStatusProbe(HttpClient http) { _http = http; }

    private readonly record struct Snapshot(long BytesReceived, long BytesSent, int Readers);

    public async Task<Dictionary<string, PublishStatus>> SampleAsync(
        IEnumerable<string> streamPaths, CancellationToken ct)
    {
        var result = streamPaths.ToDictionary(p => p, _ => PublishStatus.Unknown);

        Dictionary<string, Snapshot> current;
        try
        {
            using var resp = await _http.GetAsync("/v3/paths/list", ct);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);

            current = new();
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var name  = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var rin   = item.TryGetProperty("bytesReceived", out var br) ? br.GetInt64() : 0L;
                    var rout  = item.TryGetProperty("bytesSent",     out var bs) ? bs.GetInt64() : 0L;
                    var rdrs  = item.TryGetProperty("readers", out var r) && r.ValueKind == JsonValueKind.Array
                                ? r.GetArrayLength() : 0;
                    if (name is not null) current[name] = new Snapshot(rin, rout, rdrs);
                }
            }
        }
        catch (HttpRequestException) { return result; }   // unreachable → all unknown
        catch (TaskCanceledException) { return result; }
        catch (JsonException)         { return result; }

        foreach (var path in result.Keys)
        {
            if (!current.TryGetValue(path, out var now))
            {
                result[path] = PublishStatus.DegradedNoFrames; // path missing from MediaMTX
                continue;
            }

            if (_last.TryGetValue(path, out var prev))
            {
                var inDelta  = now.BytesReceived - prev.BytesReceived;
                var outDelta = now.BytesSent     - prev.BytesSent;

                if (inDelta <= 0)
                {
                    result[path] = PublishStatus.DegradedNoFrames;
                }
                else if (outDelta <= 0)
                {
                    result[path] = PublishStatus.DegradedAuth;
                }
                else
                {
                    result[path] = now.Readers >= 1
                        ? PublishStatus.Publishing
                        : PublishStatus.DegradedAuth;
                }
            }
            // else: first sample → leave as Unknown
            _last[path] = (now.BytesReceived, now.BytesSent);
        }
        // Prune stale baselines for removed paths.
        foreach (var stale in _last.Keys.Except(result.Keys).ToList())
            _last.Remove(stale);

        return result;
    }
}
