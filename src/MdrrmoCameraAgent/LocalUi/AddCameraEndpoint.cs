using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Mtx;
using MdrrmoCameraAgent.Probe;
using Microsoft.AspNetCore.Http;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.LocalUi;

[SupportedOSPlatform("windows")]
public sealed class AddCameraEndpoint(
    CamerasApiClient camerasApi,
    Func<string> getJwt,
    string cameraCredsDir,
    string mediaMtxYmlPath,
    MediaMtxRunner? runner)
{
    public async Task HandleAsync(HttpContext ctx)
    {
        AddCameraRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<AddCameraRequest>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ctx.RequestAborted);
        }
        catch
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid JSON body" });
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.RtspUrl))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "rtsp_url is required" });
            return;
        }

        if (!Uri.TryCreate(req.RtspUrl, UriKind.Absolute, out var rtspUri)
            || !rtspUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "rtsp_url must be a valid rtsp:// URI" });
            return;
        }

        var probe = await RtspProbe.ProbeAsync(rtspUri, TimeSpan.FromSeconds(5), ctx.RequestAborted);
        if (probe.Status == RtspProbeStatus.Unreachable)
        {
            ctx.Response.StatusCode = 422;
            await ctx.Response.WriteAsJsonAsync(new { error = "RTSP host unreachable — verify IP/port and firewall" });
            return;
        }
        if (probe.Status == RtspProbeStatus.AuthRequired)
        {
            ctx.Response.StatusCode = 422;
            await ctx.Response.WriteAsJsonAsync(new { error = "RTSP credentials rejected — re-check NVR username/password" });
            return;
        }

        var cameraId   = Guid.NewGuid().ToString();
        var credsPath  = Path.Combine(cameraCredsDir, $"cam-{cameraId}.dpapi");
        Storage.DpapiVault.WriteString(credsPath, req.RtspUrl);

        JsonElement inserted;
        try
        {
            inserted = await camerasApi.InsertCameraAsync(
                getJwt(),
                new
                {
                    name          = req.Label ?? req.RtspUrl,
                    public_label  = req.PublicLabel,
                    audio_enabled = req.AudioEnabled,
                },
                ctx.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            ctx.Response.StatusCode = 502;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
            return;
        }

        var streamPath = inserted.GetProperty("stream_path").GetString()!;
        var id         = inserted.GetProperty("id").GetString()!;

        // Build a CameraEntry with a placeholder WHIP URL; the real WHIP URL is
        // managed by PublishTokenRefresher in later waves — for now a round-trip
        // through mediamtx.yml just needs a non-null value.
        var entries = new List<CameraEntry>
        {
            new(cameraId, streamPath, req.RtspUrl, string.Empty)
        };
        MediaMtxConfigWriter.WriteToFile(mediaMtxYmlPath, entries);

        if (runner is not null)
        {
            try { await runner.ReloadAsync(entries, ctx.RequestAborted); }
            catch { /* MediaMTX not yet running in early waves — ignore */ }
        }

        ctx.Response.StatusCode = 201;
        await ctx.Response.WriteAsJsonAsync(new { id, stream_path = streamPath });
    }
}

public sealed record AddCameraRequest(
    [property: JsonPropertyName("rtsp_url")]     string?  RtspUrl,
    [property: JsonPropertyName("label")]         string?  Label,
    [property: JsonPropertyName("public_label")]  string?  PublicLabel,
    [property: JsonPropertyName("audio_enabled")] bool     AudioEnabled);
