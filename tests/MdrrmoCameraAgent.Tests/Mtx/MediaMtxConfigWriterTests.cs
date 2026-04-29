using FluentAssertions;
using MdrrmoCameraAgent.Mtx;

namespace MdrrmoCameraAgent.Tests.Mtx;

public class MediaMtxConfigWriterTests
{
    [Fact]
    public void Write_EmitsOnePathPerCamera_WithRtspSourceAndFfmpegWhipPublish()
    {
        var cams = new[]
        {
            new CameraEntry(
                Id: "cam-1",
                StreamPath: "muni-infanta/cam-1",
                RtspUrl:    "rtsp://admin:pw@192.168.1.10:554/Streaming/Channels/101",
                WhipUrl:    "https://hub/muni-infanta/cam-1/whip?jwt=tok"),
        };

        var yml = MediaMtxConfigWriter.Render(cams);

        yml.Should().Contain("paths:");
        yml.Should().Contain("muni-infanta/cam-1:");
        yml.Should().Contain("rtsp://admin:pw@192.168.1.10:554/Streaming/Channels/101");
        // ffmpeg pulls from MediaMTX's local RTSP server (not from the upstream camera)…
        yml.Should().Contain("rtsp://127.0.0.1:8554/$MTX_PATH");
        // …and re-publishes upstream via WHIP.
        yml.Should().Contain("-f whip");
        yml.Should().Contain("https://hub/muni-infanta/cam-1/whip?jwt=tok");
        // The fabricated mediamtx-publisher binary must never appear.
        yml.Should().NotContain("mediamtx-publisher");
    }

    [Fact]
    public void Write_WithNoCameras_EmitsRegistrationOnlyStub()
    {
        var yml = MediaMtxConfigWriter.Render(Array.Empty<CameraEntry>());

        yml.Should().Contain("# Auto-generated");
        yml.Should().Contain("paths: {}");
        yml.Should().NotContain("source:");
        yml.Should().NotContain("runOnReady");
    }

    [Fact]
    public void Write_SkipsCamerasWithNullWhipUrl_ButKeepsConfigValid()
    {
        var cams = new[]
        {
            new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1.2.3.4:554/s", WhipUrl: null),
        };

        var yml = MediaMtxConfigWriter.Render(cams);

        yml.Should().Contain("paths: {}");
        yml.Should().NotContain("muni-x/cam-1");
        yml.Should().NotContain("runOnReady");
    }

    [Fact]
    public void Write_EscapesRtspCredentialsContainingAtColonSlash()
    {
        var cams = new[]
        {
            new CameraEntry(
                Id: "cam-2",
                StreamPath: "muni-x/cam-2",
                RtspUrl:    "rtsp://admin:p%40ss%2Fword%3A1@10.0.0.5:554/Streaming/Channels/101",
                WhipUrl:    "https://hub/muni-x/cam-2/whip?jwt=t"),
        };

        var yml = MediaMtxConfigWriter.Render(cams);

        yml.Should().Contain("rtsp://admin:p%40ss%2Fword%3A1@10.0.0.5:554/Streaming/Channels/101");
        yml.Should().NotContain("p@ss/word:1"); // raw form must not leak
    }

    [Fact]
    public void Render_ThrowsOnNewlineInStreamPath()
    {
        var cams = new[]
        {
            new CameraEntry("cam-3", "path\ninjected", "rtsp://1.2.3.4/s", "https://hub/whip"),
        };

        var act = () => MediaMtxConfigWriter.Render(cams);
        act.Should().Throw<ArgumentException>().WithMessage("*newline*");
    }

    [SkippableFact]
    public async Task EmptyCamerasYml_LoadsCleanlyInRealMediaMtx()
    {
        var here     = AppContext.BaseDirectory;
        var mediamtx = Path.Combine(here, "mediamtx", "mediamtx.exe");
        Skip.IfNot(File.Exists(mediamtx),
            "skipped: bundled mediamtx.exe not in test bin output. Run pwsh ./scripts/fetch-mediamtx.ps1.");

        var ymlPath = Path.Combine(Path.GetTempPath(), $"mtx-parse-{Guid.NewGuid():N}.yml");
        File.WriteAllText(ymlPath, MediaMtxConfigWriter.Render(Array.Empty<CameraEntry>()));

        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mediamtx, ymlPath)
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardError  = true,
            })!;

            var started = !p.WaitForExit(2_000);
            if (!started)
            {
                var err = await p.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"mediamtx.exe exited within 2s parsing the empty-cameras yml (exit={p.ExitCode}): {err}");
            }
            p.Kill(entireProcessTree: true);
        }
        finally { try { File.Delete(ymlPath); } catch { } }
    }
}
