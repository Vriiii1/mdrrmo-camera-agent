using System.Net.Sockets;
using System.Text;

namespace MdrrmoCameraAgent.Probe;

public enum RtspProbeStatus
{
    Ok,           // 200 OK on OPTIONS
    Unreachable,  // TCP connect failed or timed out
    AuthRequired, // 401 Unauthorized — credentials rejected
    Other,        // any other response
}

public sealed record RtspProbeResult(RtspProbeStatus Status, string? RawStatusLine);

public static class RtspProbe
{
    public static async Task<RtspProbeResult> ProbeAsync(Uri rtspUrl, TimeSpan timeout, CancellationToken ct)
    {
        var port = rtspUrl.Port == -1 ? 554 : rtspUrl.Port;
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await tcp.ConnectAsync(rtspUrl.Host, port, cts.Token);
            using var stream = tcp.GetStream();
            stream.ReadTimeout  = (int)timeout.TotalMilliseconds;
            stream.WriteTimeout = (int)timeout.TotalMilliseconds;

            var reqStr = $"OPTIONS rtsp://{rtspUrl.Host}:{port}{rtspUrl.AbsolutePath} RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: MdrrmoCameraAgent\r\n\r\n";
            var reqBytes = Encoding.ASCII.GetBytes(reqStr);
            await stream.WriteAsync(reqBytes, cts.Token);

            var buf = new byte[256];
            int read = await stream.ReadAsync(buf, cts.Token);
            if (read <= 0) return new RtspProbeResult(RtspProbeStatus.Other, null);

            var line = Encoding.ASCII.GetString(buf, 0, read).Split('\r', '\n')[0];
            if (line.Contains(" 200 ")) return new(RtspProbeStatus.Ok, line);
            if (line.Contains(" 401 ")) return new(RtspProbeStatus.AuthRequired, line);
            return new(RtspProbeStatus.Other, line);
        }
        catch (OperationCanceledException) { return new(RtspProbeStatus.Unreachable, "timeout"); }
        catch (SocketException ex)         { return new(RtspProbeStatus.Unreachable, ex.Message); }
        catch (IOException ex)             { return new(RtspProbeStatus.Unreachable, ex.Message); }
    }

    public static async Task<bool> CanReachAsync(Uri rtspUrl, TimeSpan timeout, CancellationToken ct)
        => (await ProbeAsync(rtspUrl, timeout, ct)).Status != RtspProbeStatus.Unreachable;
}
