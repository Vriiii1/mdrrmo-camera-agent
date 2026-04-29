using FluentAssertions;
using MdrrmoCameraAgent.Mtx;
using System.Diagnostics;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace MdrrmoCameraAgent.Tests;

[Collection("SequentialIntegration")]
[Trait("Category", "Integration")]
public class Wave4IntegrationTests : IDisposable
{
    private readonly string _runtimeRoot = Path.Combine(Path.GetTempPath(), $"mtx-int-{Guid.NewGuid():N}");
    private readonly WireMockServer _hub = WireMockServer.Start();

    public Wave4IntegrationTests()
    {
        Directory.CreateDirectory(_runtimeRoot);
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_RUNTIME_ROOT", _runtimeRoot);
    }

    [SkippableFact]
    public async Task EndToEnd_FfmpegPattern_BytesRising_AndWireMockReceivesWhipPost()
    {
        var mediamtx = LocateBinary("mediamtx.exe");
        var ffmpeg   = LocateBinary("ffmpeg.exe");
        Skip.If(mediamtx is null || ffmpeg is null,
            "skipping: bundled mediamtx.exe / ffmpeg.exe not in test bin output. " +
            "Run `pwsh ./scripts/fetch-mediamtx.ps1 && pwsh ./scripts/fetch-ffmpeg.ps1` first.");

        // Stub WHIP endpoint — returns 201 + Location header so ffmpeg believes the publish succeeded.
        _hub.Given(Request.Create().WithPath("/muni-x/cam-1/whip").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201)
                .WithHeader("Location", "/muni-x/cam-1/whip/session-1")
                .WithHeader("Content-Type", "application/sdp")
                .WithBody("v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n"));

        // Generate yml driving ffmpeg → WireMock instead of a real WHIP server.
        var ymlPath = Path.Combine(_runtimeRoot, "runtime", "mediamtx.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(ymlPath)!);

        var cam = new CameraEntry(
            Id:         "cam-1",
            StreamPath: "muni-x/cam-1",
            RtspUrl:    "publisher",
            WhipUrl:    $"{_hub.Url}/muni-x/cam-1/whip");

        File.WriteAllText(ymlPath, MediaMtxConfigWriter.Render(new[] { cam }));

        await using var runner = new MediaMtxRunner(
            exePath: mediamtx!,
            configPath: ymlPath,
            wipeOnDispose: true);
        await runner.StartAsync();

        // Push a 5s synthetic stream into mediamtx's RTSP server.
        using var pushProc = Process.Start(new ProcessStartInfo(ffmpeg!,
            $"-re -f lavfi -i testsrc=size=320x240:rate=10 -t 5 -c:v libx264 -preset ultrafast " +
            $"-f rtsp rtsp://127.0.0.1:8554/muni-x/cam-1")
        { CreateNoWindow = true, UseShellExecute = false });
        if (pushProc is null) throw new InvalidOperationException("ffmpeg push failed to start");

        try
        {
            // Wait for the state-machine probe to see traffic on BOTH legs.
            var probe = new PublishStatusProbe(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9997") });
            var sw = Stopwatch.StartNew();
            PublishStatus latest = PublishStatus.Unknown;
            while (sw.Elapsed < TimeSpan.FromSeconds(30))
            {
                var snap = await probe.SampleAsync(new[] { "muni-x/cam-1" }, CancellationToken.None);
                latest = snap["muni-x/cam-1"];
                if (latest == PublishStatus.Publishing) break;
                await Task.Delay(1500);
            }

            // Inbound leg: camera → mediamtx must be flowing.
            latest.Should().BeOneOf(
                new[] { PublishStatus.Publishing, PublishStatus.DegradedAuth },
                "the synthetic ffmpeg push must produce rising bytesReceived in mediamtx within 30s — " +
                "DegradedAuth is acceptable here because WireMock's stubbed SDP may fail ICE/DTLS, " +
                "but DegradedNoFrames or Unknown means the inbound push didn't reach mediamtx at all");

            // Outbound leg: WireMock MUST have received at least one WHIP POST.
            // Short-circuit: WHIP POST likely already arrived during the probe window
            // (runOnReady fires as soon as mediamtx signals ready, which is within the inbound window).
            var whipPosts = GetWhipPosts();
            if (!whipPosts.Any())
            {
                // Wait for whatever budget remains (minimum 2s floor so we don't skip immediately).
                var remaining = TimeSpan.FromSeconds(30) - sw.Elapsed;
                if (remaining < TimeSpan.FromSeconds(2)) remaining = TimeSpan.FromSeconds(2);
                var whipSw = Stopwatch.StartNew();
                while (whipSw.Elapsed < remaining)
                {
                    whipPosts = GetWhipPosts();
                    if (whipPosts.Any()) break;
                    await Task.Delay(300, CancellationToken.None);
                }
            }

            whipPosts.Should().NotBeEmpty(
                "the runOnReady ffmpeg should have POSTed at least one WHIP offer to the stub hub; " +
                "no POSTs means the publish-loop outbound side never got off the ground");
        }
        finally
        {
            try { pushProc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        }
    }

    private List<WireMock.Logging.ILogEntry> GetWhipPosts() =>
        _hub.LogEntries
            .Where(e => e.RequestMessage?.Method == "POST" &&
                        e.RequestMessage.Path?.Contains("/whip", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

    private static string? LocateBinary(string name)
    {
        var here = AppContext.BaseDirectory;
        var candidate = Path.Combine(here, "mediamtx", name);
        return File.Exists(candidate) ? candidate : null;
    }

    public void Dispose()
    {
        _hub.Stop();
        try { Directory.Delete(_runtimeRoot, recursive: true); } catch { }
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_RUNTIME_ROOT", null);
    }
}
