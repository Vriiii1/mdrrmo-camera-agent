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
}
