using System.Runtime.Versioning;
using System.Text.Json;
using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Config;
using MdrrmoCameraAgent.Lifecycle;
using MdrrmoCameraAgent.Storage;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Lifecycle;

[Collection("SequentialIntegration")]
public class MinimumViableAgentTests : IAsyncLifetime
{
    private WireMockServer _api      = null!;   // dashboard
    private WireMockServer _supabase = null!;

    public Task InitializeAsync()
    {
        _api      = WireMockServer.Start();
        _supabase = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _api.Stop();
        _supabase.Stop();
        return Task.CompletedTask;
    }

    /// Build a minimal alg:none JWT that JwtCache can decode.
    /// Uses the same MintTestJwt pattern proven in JwtCacheTests.
    private static string MakeTestJwt()
    {
        var header  = "{\"alg\":\"none\"}";
        var payload = "{\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                    + ",\"agent_id\":\"aaa\",\"municipality_id\":\"infanta\",\"user_role\":\"camera_agent\"}";
        string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64(header)}.{B64(payload)}.";
    }

    /// Wires up WireMock stubs shared by tests that need bootstrap to succeed.
    private void StubBootstrap(string jwt)
    {
        _api.Given(Request.Create().WithPath("/api/v1/agents/enrollment").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new {
                agent_id = "aaa", agent_secret = "sss", hub_base_url = (string?)null }));

        _supabase.Given(Request.Create().WithPath("/rest/v1/rpc/auth_bridge_agent").UsingPost())
                 .RespondWith(Response.Create().WithStatusCode(200)
                     .WithHeader("Content-Type", "application/json")
                     .WithBody("\"" + jwt + "\""));

        _api.Given(Request.Create().WithPath("/api/v1/agents/heartbeat/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new {
                ok = true, accepted = 0, rejected = 0 }));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Run_EnrollsThenLoopsHeartbeats_UntilCancelled()
    {
        var jwt = MakeTestJwt();

        _api.Given(Request.Create().WithPath("/api/v1/agents/enrollment").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new {
                agent_id = "aaa", agent_secret = "sss", hub_base_url = "https://hub" }));

        // PostgREST returns the JWT wrapped in JSON quotes (same pattern as AuthBridgeClientTests).
        _supabase.Given(Request.Create().WithPath("/rest/v1/rpc/auth_bridge_agent").UsingPost())
                 .RespondWith(Response.Create().WithStatusCode(200)
                     .WithHeader("Content-Type", "application/json")
                     .WithBody("\"" + jwt + "\""));

        _api.Given(Request.Create().WithPath("/api/v1/agents/heartbeat/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new {
                ok = true, accepted = 0, rejected = 0 }));

        var bundle = new ProvisioningBundle(
            EnrollmentToken: "tok",
            HubBaseUrl:      "https://hub",
            JwksUrl:         null,
            ApiBaseUrl:      _api.Url!,
            SupabaseUrl:     _supabase.Url!,
            SupabaseAnonKey: "anon");

        var tempRoot        = Path.Combine(Path.GetTempPath(), "mva-" + Guid.NewGuid());
        var tempRuntimeRoot = Path.Combine(Path.GetTempPath(), "mva-rt-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT",         tempRoot);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_RUNTIME_ROOT",  tempRuntimeRoot);

            // Inject a fast 100 ms heartbeat interval so the test runs in well under a second.
            // localUiPort: 0 = OS picks an ephemeral port so we don't conflict with a
            // production service that may be running on 8787 in the dev environment.
            var agent = new MinimumViableAgent(
                bundle,
                hostname: "mdrrmo-cctv-test-host",
                heartbeatInterval: TimeSpan.FromMilliseconds(100),
                localUiPort: 0);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(1200));  // enough for bootstrap + ≥2 beats

            await agent.RunAsync(cts.Token);

            // Enrollment fired exactly once; heartbeat fired ≥ 2 times.
            _api.LogEntries
                .Count(e => e.RequestMessage?.AbsolutePath == "/api/v1/agents/enrollment")
                .Should().Be(1);

            _api.LogEntries
                .Count(e => e.RequestMessage?.AbsolutePath == "/api/v1/agents/heartbeat/")
                .Should().BeGreaterOrEqualTo(2);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            if (Directory.Exists(tempRuntimeRoot))
                Directory.Delete(tempRuntimeRoot, recursive: true);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT",         null);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_RUNTIME_ROOT",  null);
        }
    }

    // ── Step 2: new failing tests ─────────────────────────────────────────────

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task RunAsync_LogsRegistrationOnlyMode_WhenHubBaseUrlIsNull()
    {
        var jwt = MakeTestJwt();
        StubBootstrap(jwt);

        // No stub for GET /api/v1/agents/cameras — WireMock returns 404 by default,
        // which TryRefreshCamerasFromServerAsync catches and WARNs about.

        var bundle = new ProvisioningBundle(
            EnrollmentToken: "tok",
            HubBaseUrl:      null,
            JwksUrl:         null,
            ApiBaseUrl:      _api.Url!,
            SupabaseUrl:     _supabase.Url!,
            SupabaseAnonKey: "anon");

        var tempRoot = Path.Combine(Path.GetTempPath(), "mva-reg-only-" + Guid.NewGuid());
        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", tempRoot);

            var agent = new MinimumViableAgent(bundle, "test-host",
                heartbeatInterval: TimeSpan.FromMilliseconds(50),
                localUiPort: 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            try { await agent.RunAsync(cts.Token); }
            catch (OperationCanceledException) { /* expected */ }
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
        }

        stdout.ToString().Should().Contain("registration-only mode");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task RunAsync_DoesNotCrash_WhenCameraRefreshReturns503()
    {
        var jwt = MakeTestJwt();
        StubBootstrap(jwt);

        // Stub GET /api/v1/agents/cameras → 503
        _api.Given(Request.Create().WithPath("/api/v1/agents/cameras/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("Service Unavailable"));

        var bundle = new ProvisioningBundle(
            EnrollmentToken: "tok",
            HubBaseUrl:      null,
            JwksUrl:         null,
            ApiBaseUrl:      _api.Url!,
            SupabaseUrl:     _supabase.Url!,
            SupabaseAnonKey: "anon");

        var tempRoot    = Path.Combine(Path.GetTempPath(), "mva-503-" + Guid.NewGuid());
        var originalErr = Console.Error;
        var stderr      = new StringWriter();
        Console.SetError(stderr);

        Exception? thrown = null;
        try
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", tempRoot);

            // Pre-create tempRoot with an empty cameras.json (valid JSON, no entries).
            // The agent must NOT overwrite it with anything on a 503 error.
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "cameras.json"), "[]");

            var agent = new MinimumViableAgent(bundle, "test-host",
                heartbeatInterval: TimeSpan.FromMilliseconds(50),
                localUiPort: 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            try { await agent.RunAsync(cts.Token); }
            catch (OperationCanceledException) { /* expected */ }
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        finally
        {
            Console.SetError(originalErr);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
        }

        thrown.Should().BeNull("agent must not crash on 503 from cameras refresh");
        stderr.ToString().Should().Contain("WARN");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task RunAsync_WritesCamerasJson_WhenRefreshReturns200WithTwoCameras()
    {
        var jwt = MakeTestJwt();
        StubBootstrap(jwt);

        var twocameras = new[]
        {
            new { id = "cam-aaa", stream_path = "muni-infanta/cam-aaa", rtsp_url = "rtsp://192.168.1.10:554/stream", whip_url = (string?)null },
            new { id = "cam-bbb", stream_path = "muni-infanta/cam-bbb", rtsp_url = "rtsp://192.168.1.11:554/stream", whip_url = (string?)null },
        };

        _api.Given(Request.Create().WithPath("/api/v1/agents/cameras/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { data = twocameras }));

        var bundle = new ProvisioningBundle(
            EnrollmentToken: "tok",
            HubBaseUrl:      null,
            JwksUrl:         null,
            ApiBaseUrl:      _api.Url!,
            SupabaseUrl:     _supabase.Url!,
            SupabaseAnonKey: "anon");

        var tempRoot = Path.Combine(Path.GetTempPath(), "mva-refresh-" + Guid.NewGuid());
        string? camerasFilePath = null;
        string? camerasJson = null;
        try
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", tempRoot);

            var agent = new MinimumViableAgent(bundle, "test-host",
                heartbeatInterval: TimeSpan.FromMilliseconds(50),
                localUiPort: 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            try { await agent.RunAsync(cts.Token); }
            catch (OperationCanceledException) { /* expected */ }

            camerasFilePath = Path.Combine(tempRoot, "cameras.json");
            if (File.Exists(camerasFilePath))
                camerasJson = File.ReadAllText(camerasFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
        }

        camerasJson.Should().NotBeNull("cameras.json should have been written after a successful refresh");
        var parsed = JsonSerializer.Deserialize<JsonElement[]>(camerasJson!);
        parsed.Should().HaveCount(2);
        parsed![0].GetProperty("Id").GetString().Should().Be("cam-aaa");
        parsed![1].GetProperty("Id").GetString().Should().Be("cam-bbb");
    }
}
