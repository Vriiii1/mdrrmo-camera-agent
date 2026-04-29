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

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _fn;
        public TestHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(_fn(req, ct));
    }
}
