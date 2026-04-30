using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using System.Net.Http.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Backend;

public class HeartbeatClientTests : IAsyncLifetime
{
    private WireMockServer _s = null!;
    public Task InitializeAsync() { _s = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _s.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task Send_PostsAuthorizedHeartbeat()
    {
        _s.Given(Request.Create()
                .WithPath("/api/v1/agents/heartbeat/")
                .UsingPost()
                .WithHeader("Authorization", "Bearer fake-jwt"))
          .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { ok = true, accepted = 1, rejected = 0 }));

        var c = new HeartbeatClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        var r = await c.SendAsync("fake-jwt",
            new[] { new HeartbeatCamera("uuid-1", "online", DateTimeOffset.UtcNow) },
            default);

        r.Ok.Should().BeTrue();
        r.Accepted.Should().Be(1);
        r.Rejected.Should().Be(0);
    }

    [Fact]
    public async Task Send_ThrowsHeartbeatException_OnNon2xx()
    {
        _s.Given(Request.Create().WithPath("/api/v1/agents/heartbeat/").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(401).WithBody("Unauthorized"));

        var c = new HeartbeatClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        await FluentActions.Awaiting(() =>
                c.SendAsync("bad-jwt", Array.Empty<HeartbeatCamera>(), default))
            .Should().ThrowAsync<HeartbeatException>()
            .WithMessage("*401*");
    }

    [Fact]
    public async Task SendAsync_IncludesPublishStatus_WhenProvided()
    {
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { ok = true, accepted = 1, rejected = 0 })
            });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new HeartbeatClient(http);

        await client.SendAsync("jwt",
            new[] { new HeartbeatCamera("cam-1", "online", DateTimeOffset.UtcNow, PublishStatus: "publishing") },
            CancellationToken.None);

        handler.LastRequestBody.Should().Contain("\"publish_status\":\"publishing\"");
    }

    [Fact]
    public async Task SendAsync_OmitsPublishStatus_WhenNull()
    {
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { ok = true, accepted = 0, rejected = 0 })
            });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new HeartbeatClient(http);

        await client.SendAsync("jwt",
            new[] { new HeartbeatCamera("cam-1", "online", null, PublishStatus: null) },
            CancellationToken.None);

        handler.LastRequestBody.Should().NotContain("publish_status");
    }

    [Fact]
    public async Task Heartbeat_RequestUriEndsWithSlash_ToAvoidNextJsRedirect()
    {
        // Regression guard for v0.4.1 Bug #1.
        // super-admin-landing/next.config.ts sets `trailingSlash: true`. If the
        // agent posts to "/api/v1/agents/heartbeat" (no slash) Next.js answers
        // with 308 -> "/heartbeat/", and .NET HttpClient strips the manually
        // set Authorization header when following the redirect, producing a
        // 401 at the route handler. The fix is to bake the trailing slash into
        // the request URI itself. This test fails if a future contributor
        // edits the URI back to no-trailing-slash.
        _s.Given(Request.Create()
                .WithPath("/api/v1/agents/heartbeat/")
                .UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { ok = true, accepted = 0, rejected = 0 }));

        var c = new HeartbeatClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        for (var i = 0; i < 5; i++)
        {
            await c.SendAsync("fake-jwt",
                new[] { new HeartbeatCamera($"uuid-{i}", "online", DateTimeOffset.UtcNow) },
                default);
        }

        var heartbeatHits = _s.LogEntries
            .Where(e => e.RequestMessage?.Method == "POST")
            .Select(e => e.RequestMessage!.AbsolutePath)
            .ToList();

        heartbeatHits.Should().HaveCount(5);
        heartbeatHits.Should().OnlyContain(p => p!.EndsWith("/"),
            because: "every heartbeat request URI must end with '/' to bypass " +
                     "Next.js trailingSlash redirects that would strip the auth header");
    }

    private sealed class CapturingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken _)
        {
            LastRequestBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync()
                : null;
            return respond(request);
        }
    }
}
