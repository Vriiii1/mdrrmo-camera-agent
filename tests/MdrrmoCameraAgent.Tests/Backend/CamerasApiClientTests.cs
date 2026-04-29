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
    }
}
