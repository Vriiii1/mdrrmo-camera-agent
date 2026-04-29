using FluentAssertions;
using MdrrmoCameraAgent.Mtx;
using System.Diagnostics;

namespace MdrrmoCameraAgent.Tests.Mtx;

public class MediaMtxRunnerTests
{
    // timeout.exe exits immediately when spawned without an attached console;
    // ping.exe -n 60 127.0.0.1 is a reliable console-free long-running substitute.
    private static readonly string Sleeper = Path.Combine(Environment.SystemDirectory, "ping.exe");
    private static readonly string[] SleeperArgs = new[] { "-n", "60", "127.0.0.1" };

    [Fact]
    public async Task StartAndStop_ManagesChildProcessLifecycle()
    {
        var runner = new MediaMtxRunner(
            exePath:    Sleeper,
            configPath: "ignored",
            args:       SleeperArgs);

        await runner.StartAsync();
        runner.IsRunning.Should().BeTrue();

        await runner.StopAsync();
        runner.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Watchdog_RestartsChildAfterUnexpectedExit()
    {
        var runner = new MediaMtxRunner(
            exePath:    Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            configPath: "ignored",
            args:       new[] { "/c", "exit", "0" },
            initialBackoff: TimeSpan.FromMilliseconds(50),
            maxBackoff:     TimeSpan.FromMilliseconds(200));

        await runner.StartAsync();
        await Task.Delay(800);

        runner.CrashCount.Should().BeGreaterOrEqualTo(2);

        await runner.StopAsync();
    }

    [Fact]
    public async Task ReloadAsync_PostsToControlApi_WithReplaceEndpoint()
    {
        var captured = new List<HttpRequestMessage>();
        var handler  = new TestHandler((req, _) => {
            captured.Add(req);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var runner = new MediaMtxRunner(
            exePath:    Sleeper,
            configPath: "ignored",
            args:       SleeperArgs,
            controlApiHandler: handler);

        await runner.StartAsync();
        try
        {
            await runner.ReloadAsync(new[] {
                new CameraEntry("cam-a", "muni-x/cam-a", "rtsp://1.2.3.4:554/s", "https://hub/muni-x/cam-a/whip?jwt=t"),
            });
            captured.Should().ContainSingle();
            captured[0].Method.Should().Be(HttpMethod.Post);
            captured[0].RequestUri!.AbsolutePath.Should().StartWith("/v3/config/paths/");
        }
        finally { await runner.StopAsync(); }
    }

    [Fact]
    public async Task DisposeAsync_WipesYmlFromDisk()
    {
        var ymlPath = Path.Combine(Path.GetTempPath(), $"mtx-test-{Guid.NewGuid():N}.yml");
        File.WriteAllText(ymlPath, "# decrypted creds inside\nsource: rtsp://admin:secret@1.2.3.4");

        await using (var runner = new MediaMtxRunner(
            exePath:    Sleeper,
            configPath: ymlPath,
            args:       SleeperArgs,
            wipeOnDispose: true))
        {
            await runner.StartAsync();
        } // dispose

        File.Exists(ymlPath).Should().BeFalse(
            "the runner must wipe the yml so DPAPI-decrypted creds don't survive shutdown");
    }

    [Fact]
    public async Task MultipleRestarts_DoNotLeakCts_AndRunnerSurvives()
    {
        var runner = new MediaMtxRunner(
            exePath:        Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            configPath:     "ignored",
            args:           new[] { "/c", "exit", "0" },
            initialBackoff: TimeSpan.FromMilliseconds(20),
            maxBackoff:     TimeSpan.FromMilliseconds(50),
            logFilePath:    Path.Combine(Path.GetTempPath(), $"mtx-restart-{Guid.NewGuid():N}.log"));

        await runner.StartAsync();
        await Task.Delay(300); // allow several crash-restart cycles
        runner.CrashCount.Should().BeGreaterOrEqualTo(3,
            "the runner should have restarted multiple times without leaking CTS handles");
        await runner.StopAsync();
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotWipeYml_WhenWipeOnDisposeFalse()
    {
        var ymlPath = Path.Combine(Path.GetTempPath(), $"mtx-test-keep-{Guid.NewGuid():N}.yml");
        File.WriteAllText(ymlPath, "# kept");

        await using (var runner = new MediaMtxRunner(
            exePath:    Sleeper,
            configPath: ymlPath,
            args:       SleeperArgs,
            wipeOnDispose: false))
        {
            await runner.StartAsync();
        }

        File.Exists(ymlPath).Should().BeTrue();
        File.Delete(ymlPath); // cleanup
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _fn;
        public TestHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(_fn(req, ct));
    }
}
