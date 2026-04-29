using FluentAssertions;
using MdrrmoCameraAgent.Lifecycle;

namespace MdrrmoCameraAgent.Tests.Lifecycle;

/// <summary>
/// Contract tests for <see cref="ServiceInstaller"/>.
/// Uses a hand-written fake for <see cref="IProcessRunner"/> — no real OS process is spawned.
/// </summary>
public class ServiceInstallerTests
{
    // ── Fake ─────────────────────────────────────────────────────────────────

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly List<(string Exe, string[] Args)> _calls = new();

        public IReadOnlyList<(string Exe, string[] Args)> Calls => _calls;

        public Task RunAsync(string exe, string[] args, CancellationToken ct)
        {
            _calls.Add((exe, args));
            return Task.CompletedTask;
        }
    }

    // ── InstallAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallAsync_CallsInstallThenStart_InOrder()
    {
        var fake      = new FakeProcessRunner();
        var installer = new ServiceInstaller(fake);
        const string winswExe  = @"C:\install\winsw.exe";
        const string serviceXml = @"C:\install\MdrrmoCameraAgent.xml";

        await installer.InstallAsync(winswExe, serviceXml, CancellationToken.None);

        fake.Calls.Should().HaveCount(2);

        // First call: winsw install <xml>
        fake.Calls[0].Exe.Should().Be(winswExe);
        fake.Calls[0].Args.Should().Equal("install", serviceXml);

        // Second call: winsw start <xml>
        fake.Calls[1].Exe.Should().Be(winswExe);
        fake.Calls[1].Args.Should().Equal("start", serviceXml);
    }

    [Fact]
    public async Task InstallAsync_PassesCancellationToken_ThroughToRunner()
    {
        // Arrange: a runner that captures the token it receives
        var capturedTokens = new List<CancellationToken>();
        var capturingRunner = new CapturingProcessRunner(capturedTokens);
        var installer = new ServiceInstaller(capturingRunner);

        using var cts = new CancellationTokenSource();

        // Act
        await installer.InstallAsync("winsw.exe", "svc.xml", cts.Token);

        // Assert: both calls received the same token
        capturedTokens.Should().HaveCount(2);
        capturedTokens.All(t => t == cts.Token).Should().BeTrue();
    }

    // ── UninstallAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UninstallAsync_CallsStopThenUninstall_InOrder()
    {
        var fake      = new FakeProcessRunner();
        var installer = new ServiceInstaller(fake);
        const string winswExe   = @"C:\install\winsw.exe";
        const string serviceXml = @"C:\install\MdrrmoCameraAgent.xml";

        await installer.UninstallAsync(winswExe, serviceXml, CancellationToken.None);

        fake.Calls.Should().HaveCount(2);

        // First call: winsw stop <xml>
        fake.Calls[0].Exe.Should().Be(winswExe);
        fake.Calls[0].Args.Should().Equal("stop", serviceXml);

        // Second call: winsw uninstall <xml>
        fake.Calls[1].Exe.Should().Be(winswExe);
        fake.Calls[1].Args.Should().Equal("uninstall", serviceXml);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallAsync_WhenRunnerThrowsOperationCancelled_PropagatesException()
    {
        var cancellingRunner = new CancellingProcessRunner();
        var installer = new ServiceInstaller(cancellingRunner);

        Func<Task> act = () => installer.InstallAsync("winsw.exe", "svc.xml", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Only the first call should have been made — install should not proceed to start.
        cancellingRunner.CallCount.Should().Be(1);
    }

    // ── Helper fakes ─────────────────────────────────────────────────────────

    private sealed class CapturingProcessRunner(List<CancellationToken> capturedTokens) : IProcessRunner
    {
        public Task RunAsync(string exe, string[] args, CancellationToken ct)
        {
            capturedTokens.Add(ct);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task RunAsync(string exe, string[] args, CancellationToken ct)
        {
            CallCount++;
            throw new OperationCanceledException("simulated cancellation");
        }
    }
}
