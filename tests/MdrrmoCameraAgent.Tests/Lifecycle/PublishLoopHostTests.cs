using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Lifecycle;
using MdrrmoCameraAgent.Mtx;

namespace MdrrmoCameraAgent.Tests.Lifecycle;

public class PublishLoopHostTests
{
    [Fact]
    public void SnapshotHeartbeatPayload_DefaultsToUnknown_WhenProbeHasntRun()
    {
        var host = new PublishLoopHost(
            cameras: new[] {
                new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1.2.3.4/s", null),
            });

        var payload = host.SnapshotHeartbeatPayload();

        payload.Should().HaveCount(1);
        payload[0].Id.Should().Be("cam-1");
        payload[0].PublishStatus.Should().Be("unknown");
    }

    [Fact]
    public void RecordProbeResult_PropagatesToNextSnapshot()
    {
        var host = new PublishLoopHost(
            cameras: new[] {
                new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1.2.3.4/s", null),
                new CameraEntry("cam-2", "muni-x/cam-2", "rtsp://1.2.3.5/s", null),
            });

        host.RecordProbeResult(new Dictionary<string, PublishStatus>
        {
            ["muni-x/cam-1"] = PublishStatus.Publishing,
            ["muni-x/cam-2"] = PublishStatus.DegradedNoFrames,
        });

        var payload = host.SnapshotHeartbeatPayload();
        payload.Single(p => p.Id == "cam-1").PublishStatus.Should().Be("publishing");
        payload.Single(p => p.Id == "cam-2").PublishStatus.Should().Be("degraded_no_frames");
    }

    [Fact]
    public void RegistrationOnlyMode_WhenNoneOfTheCamerasHaveAWhipUrl_ReportsAllUnknown()
    {
        var host = new PublishLoopHost(
            cameras: new[] {
                new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1.2.3.4/s", WhipUrl: null),
            });

        var payload = host.SnapshotHeartbeatPayload();
        payload[0].PublishStatus.Should().Be("unknown");
    }

    [Fact]
    public void ReplaceCameras_WithIdenticalList_ReturnsFalse_NoOp()
    {
        var initial = new[] { new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null) };
        var host    = new PublishLoopHost(initial);

        var changed = host.ReplaceCameras(new[] { new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null) });

        changed.Should().BeFalse();
        host.Cameras.Should().HaveCount(1);
    }

    [Fact]
    public void ReplaceCameras_WithNewCamera_ReturnsTrue_AndAppearsInSnapshot()
    {
        var host = new PublishLoopHost(new[] {
            new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null),
        });

        var changed = host.ReplaceCameras(new[] {
            new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null),
            new CameraEntry("cam-2", "muni-x/cam-2", "rtsp://1/b", null),
        });

        changed.Should().BeTrue();
        host.SnapshotHeartbeatPayload().Should().HaveCount(2);
        host.SnapshotHeartbeatPayload().Select(p => p.Id).Should().BeEquivalentTo(new[] { "cam-1", "cam-2" });
    }

    [Fact]
    public void ReplaceCameras_DropsStaleProbeResults_ForRemovedCameras()
    {
        var host = new PublishLoopHost(new[] {
            new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null),
            new CameraEntry("cam-2", "muni-x/cam-2", "rtsp://1/b", null),
        });
        host.RecordProbeResult(new Dictionary<string, PublishStatus>
        {
            ["muni-x/cam-1"] = PublishStatus.Publishing,
            ["muni-x/cam-2"] = PublishStatus.Publishing,
        });

        host.ReplaceCameras(new[] { new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null) });

        host.SnapshotHeartbeatPayload().Should().ContainSingle().Which.Id.Should().Be("cam-1");
    }

    [Fact]
    public void RecordProbeResult_IgnoresOrphanPaths_NotInCurrentCameraList()
    {
        var host = new PublishLoopHost(new[] {
            new CameraEntry("cam-1", "muni-x/cam-1", "rtsp://1/a", null),
        });

        // "ghost/path" is not a StreamPath for any registered camera.
        host.RecordProbeResult(new Dictionary<string, PublishStatus>
        {
            ["muni-x/cam-1"] = PublishStatus.Publishing,
            ["ghost/path"]   = PublishStatus.Publishing,
        });

        // cam-1 gets its status; "ghost/path" is silently ignored.
        var payload = host.SnapshotHeartbeatPayload();
        payload.Should().ContainSingle().Which.PublishStatus.Should().Be("publishing");

        // Now replace cameras with an empty list — no ghost entry should reappear.
        host.ReplaceCameras(Array.Empty<CameraEntry>());
        host.SnapshotHeartbeatPayload().Should().BeEmpty();
    }
}
