using System.Runtime.Versioning;
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

        var tempRoot = Path.Combine(Path.GetTempPath(), "mva-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", tempRoot);

            // Inject a fast 100 ms heartbeat interval so the test runs in well under a second.
            var agent = new MinimumViableAgent(
                bundle,
                hostname: "mdrrmo-cctv-test-host",
                heartbeatInterval: TimeSpan.FromMilliseconds(100));

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
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
        }
    }
}
