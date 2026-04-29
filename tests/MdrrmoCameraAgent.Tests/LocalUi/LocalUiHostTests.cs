using FluentAssertions;
using MdrrmoCameraAgent.LocalUi;
using System.Runtime.Versioning;

namespace MdrrmoCameraAgent.Tests.LocalUi;

[SupportedOSPlatform("windows")]
public class LocalUiHostTests
{
    [Fact]
    public async Task Host_BindsToLoopbackOnly()
    {
        var host = new LocalUiHost(port: 0);   // port 0 = OS picks ephemeral
        await host.StartAsync(default);
        try
        {
            var url = host.BoundUrl;
            url.Should().StartWith("http://127.0.0.1:");
            using var http = new HttpClient();
            var resp = await http.GetAsync(url + "/health");
            resp.IsSuccessStatusCode.Should().BeTrue();
        }
        finally { await host.StopAsync(default); }
    }
}
