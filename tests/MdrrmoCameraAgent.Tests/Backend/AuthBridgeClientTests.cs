using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Backend;

public class AuthBridgeClientTests : IAsyncLifetime
{
    private WireMockServer _s = null!;
    public Task InitializeAsync() { _s = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _s.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task MintJwt_PostsRpcAndReturnsToken()
    {
        _s.Given(Request.Create().WithPath("/rest/v1/rpc/auth_bridge_agent").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200).WithBody("\"eyJ-fake-jwt\""));

        var c = new AuthBridgeClient(
            new HttpClient { BaseAddress = new Uri(_s.Url!) },
            anonKey: "anon-xyz");

        var jwt = await c.MintJwtAsync(
            agentId: "11111111-1111-1111-1111-111111111111",
            agentSecret: "secret",
            ct: default);

        jwt.Should().Be("eyJ-fake-jwt");
    }
}
