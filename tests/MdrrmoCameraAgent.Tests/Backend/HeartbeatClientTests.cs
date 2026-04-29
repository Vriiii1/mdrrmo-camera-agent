using FluentAssertions;
using MdrrmoCameraAgent.Backend;
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
                .WithPath("/api/v1/agents/heartbeat")
                .UsingPost()
                .WithHeader("Authorization", "Bearer fake-jwt"))
          .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { ok = true, accepted = 1, rejected = 0 }));

        var c = new HeartbeatClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        var r = await c.SendAsync("fake-jwt",
            new[] { new HeartbeatCamera("uuid-1", "online", DateTimeOffset.UtcNow) },
            default);

        r.Accepted.Should().Be(1);
        r.Rejected.Should().Be(0);
    }
}
