using FluentAssertions;
using MdrrmoCameraAgent.Hikvision;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.Hikvision;

public class IsapiClientTests : IAsyncLifetime
{
    private WireMockServer _s = null!;
    public Task InitializeAsync() { _s = WireMockServer.Start(); return Task.CompletedTask; }
    public Task DisposeAsync()    { _s.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task ListChannels_ParsesFriendlyNames()
    {
        _s.Given(Request.Create().WithPath("/ISAPI/ContentMgmt/InputProxy/channels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200).WithBody(
              "<?xml version=\"1.0\"?>" +
              "<InputProxyChannelList>" +
              "<InputProxyChannel><id>1</id><name>Front Gate</name></InputProxyChannel>" +
              "<InputProxyChannel><id>2</id><name>Lobby</name></InputProxyChannel>" +
              "</InputProxyChannelList>"));

        var c = new IsapiClient(new HttpClient { BaseAddress = new Uri(_s.Url!) }, "admin", "12345");
        var chans = await c.ListChannelsAsync(default);
        chans.Should().HaveCount(2);
        chans[0].Name.Should().Be("Front Gate");
        chans[1].Name.Should().Be("Lobby");
    }

    [Fact]
    public async Task ListChannels_ToleratesMissingNameField()
    {
        _s.Given(Request.Create().WithPath("/ISAPI/ContentMgmt/InputProxy/channels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200).WithBody(
              "<?xml version=\"1.0\"?>" +
              "<InputProxyChannelList>" +
              "<InputProxyChannel><id>1</id></InputProxyChannel>" +
              "</InputProxyChannelList>"));

        var c = new IsapiClient(new HttpClient { BaseAddress = new Uri(_s.Url!) }, "admin", "12345");
        var chans = await c.ListChannelsAsync(default);
        chans[0].Name.Should().Be("Channel 1");
    }

    [Fact]
    public async Task ListChannels_ToleratesUnknownNamespace()
    {
        // Some Hikvision firmware versions add XML namespaces to the root element.
        _s.Given(Request.Create().WithPath("/ISAPI/ContentMgmt/InputProxy/channels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200).WithBody(
              "<?xml version=\"1.0\"?>" +
              "<InputProxyChannelList xmlns=\"http://www.hikvision.com/ver20/XMLSchema\">" +
              "<InputProxyChannel><id>3</id><name>Parking Lot</name></InputProxyChannel>" +
              "</InputProxyChannelList>"));

        var c = new IsapiClient(new HttpClient { BaseAddress = new Uri(_s.Url!) }, "admin", "12345");
        var chans = await c.ListChannelsAsync(default);
        chans.Should().HaveCount(1);
        chans[0].Name.Should().Be("Parking Lot");
    }
}
