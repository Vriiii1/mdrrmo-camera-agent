using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Mtx;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Backend;

public class PublishTokenClientTests : IAsyncLifetime
{
    private WireMockServer _s = null!;
    public Task InitializeAsync() { _s = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _s.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task GetUrl_ReturnsWhipUrlAndExpiry()
    {
        _s.Given(Request.Create()
                .WithPath("/api/v1/agents/streams/cam-1/publish-token")
                .UsingPost()
                .WithHeader("Authorization", "Bearer jwt"))
          .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new {
              url = "https://hub/path/whip?jwt=abc",
              expires_at = "2026-04-29T01:30:00Z" }));

        var c = new PublishTokenClient(new HttpClient { BaseAddress = new Uri(_s.Url!) });
        var r = await c.GetAsync("jwt", "cam-1", default);
        r.Url.Should().StartWith("https://hub/");
        r.ExpiresAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task RunAsync_RefreshesAtExpiresAt_MinusSafetyMargin()
    {
        var calls = new System.Collections.Concurrent.ConcurrentBag<DateTimeOffset>();

        // Fake client: returns a token expiring 200ms from now so a 50ms safety margin
        // schedules the next refresh ~150ms away.
        int callCount = 0;
        Func<string, string, CancellationToken, Task<PublishTokenResult>> fakeGet =
            (jwt, cameraId, ct) =>
            {
                callCount++;
                calls.Add(DateTimeOffset.UtcNow);
                return Task.FromResult(new PublishTokenResult(
                    "https://hub/x",
                    DateTimeOffset.UtcNow.AddMilliseconds(200)));
            };

        var refresher = new PublishTokenRefresher(
            getToken: fakeGet,
            getJwt: _ => Task.FromResult("jwt"),
            onUrlChanged: (_, _) => { },
            safetyMargin: TimeSpan.FromMilliseconds(50),
            minSleep: TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await refresher.RunAsync(new[] { "cam-a" }, cts.Token);

        calls.Count.Should().BeGreaterOrEqualTo(3); // ~150ms cadence over 700ms
    }
}
