using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Backend;

public class CamerasApiClientTests : IAsyncLifetime
{
    private WireMockServer _s = null!;
    public Task InitializeAsync() { _s = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _s.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task InsertCamera_PostsToAgentEndpointWithJwt()
    {
        _s.Given(Request.Create()
                .WithPath("/api/v1/agents/cameras/")
                .UsingPost()
                .WithHeader("Authorization", "Bearer jwt"))
          .RespondWith(Response.Create().WithStatusCode(201)
                .WithBodyAsJson(new {
                    id = "11111111-1111-1111-1111-111111111111",
                    stream_path = "muni-infanta/cam-1" }));

        var c = new CamerasApiClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        var inserted = await c.InsertCameraAsync(jwt: "jwt", new {
            name = "City Hall front gate",
            public_label = "City Hall — Public View",
            audio_enabled = false
        }, default);

        inserted.GetProperty("id").GetString().Should().Be("11111111-1111-1111-1111-111111111111");
        inserted.GetProperty("stream_path").GetString().Should().Be("muni-infanta/cam-1");

        // Security invariant: tenant identity must NEVER be sent over the wire —
        // the server derives municipality_id and agent_id from the JWT.
        var logEntry   = _s.LogEntries.Single(e => e.RequestMessage?.AbsolutePath == "/api/v1/agents/cameras/");
        var bodyText   = logEntry.RequestMessage?.Body ?? string.Empty;
        bodyText.Should().NotContain("municipality_id",
            because: "tenant identity must not be sent in the request body");
        bodyText.Should().NotContain("agent_id",
            because: "agent identity must not be sent in the request body");
    }

    [Fact]
    public async Task ListAsync_RequestUriEndsWithSlash_ToAvoidNextJsRedirect()
    {
        // Regression guard for v0.4.1 Bug #1 (companion to the heartbeat fix).
        // super-admin-landing/next.config.ts sets `trailingSlash: true`. If the
        // agent GETs "/api/v1/agents/cameras" (no slash) Next.js answers with
        // 308 -> "/cameras/" and .NET strips the Authorization header during
        // the redirect-follow, so the redirected request reaches the route
        // handler unauthenticated. InsertCameraAsync already uses the
        // trailing-slash form; this test asserts ListAsync stays in alignment.
        _s.Given(Request.Create()
                .WithPath("/api/v1/agents/cameras/")
                .UsingGet()
                .WithHeader("Authorization", "Bearer jwt"))
          .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { data = Array.Empty<object>() }));

        var c = new CamerasApiClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        await c.ListAsync(jwt: "jwt", default);

        var camerasHits = _s.LogEntries
            .Where(e => e.RequestMessage?.Method == "GET")
            .Select(e => e.RequestMessage!.AbsolutePath)
            .ToList();

        camerasHits.Should().NotBeEmpty();
        camerasHits.Should().OnlyContain(p => p!.EndsWith("/"),
            because: "the cameras list GET must end with '/' to bypass " +
                     "Next.js trailingSlash redirects that would strip the auth header");
    }
}
