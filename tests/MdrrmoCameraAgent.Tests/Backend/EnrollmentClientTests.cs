using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Backend;

public class EnrollmentClientTests : IAsyncLifetime
{
    private WireMockServer _server = null!;

    public Task InitializeAsync() { _server = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _server.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task Enroll_PostsTokenAndReturnsSecret()
    {
        _server
          .Given(Request.Create().WithPath("/api/v1/agents/enrollment").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
          {
              agent_id        = "11111111-1111-1111-1111-111111111111",
              agent_secret    = "secret-abc",
              hub_base_url    = "https://hub",
              jwks_url        = "https://hub/jwks",
              auth_bridge_url = "https://api/auth"
          }));

        var client = new EnrollmentClient(new HttpClient { BaseAddress = new Uri(_server.Url!) });
        var result = await client.EnrollAsync("tok123", "DESKTOP-XYZ", "0.0.1", default);

        result.AgentId.Should().Be("11111111-1111-1111-1111-111111111111");
        result.AgentSecret.Should().Be("secret-abc");
        result.HubBaseUrl.Should().Be("https://hub");
    }

    [Fact]
    public async Task Enroll_ThrowsOnNon2xx()
    {
        _server
          .Given(Request.Create().WithPath("/api/v1/agents/enrollment").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(401).WithBodyAsJson(new { error = "Invalid enrollment token" }));

        var client = new EnrollmentClient(new HttpClient { BaseAddress = new Uri(_server.Url!) });
        var act = () => client.EnrollAsync("bad", "host", "v", default);
        await act.Should().ThrowAsync<EnrollmentException>().WithMessage("*Invalid enrollment token*");
    }
}
