using FluentAssertions;
using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.LocalUi;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests.LocalUi;

[SupportedOSPlatform("windows")]
public class AddCameraEndpointTests : IAsyncDisposable
{
    private LocalUiHost? _host;
    private WireMockServer? _apiServer;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.StopAsync(default);
        _apiServer?.Stop();
    }

    /// <summary>
    /// Starts a fake RTSP TCP server that responds with 200 OK, starts a WireMock for
    /// the cameras API, then POSTs to /cameras and asserts 201 + id/stream_path.
    /// </summary>
    [Fact]
    public async Task PostCamera_GenericRtsp_Returns201()
    {
        // 1. Fake RTSP server
        var rtspListener = new TcpListener(IPAddress.Loopback, 0);
        rtspListener.Start();
        int rtspPort = ((IPEndPoint)rtspListener.LocalEndpoint).Port;

        var rtspServerTask = Task.Run(async () =>
        {
            using var client = await rtspListener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[512];
            await stream.ReadAsync(buf);
            var response = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n");
            await stream.WriteAsync(response);
        });

        // 2. Fake cameras API (WireMock)
        _apiServer = WireMockServer.Start();
        _apiServer.Given(
                Request.Create()
                    .WithPath("/api/v1/agents/cameras/")
                    .UsingPost()
                    .WithHeader("Authorization", "Bearer fake-jwt"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(201)
                    .WithBodyAsJson(new
                    {
                        id          = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        stream_path = "muni-test/cam-x",
                    }));

        // 3. LocalUiHost wired with all dependencies
        var credsDir      = Path.Combine(Path.GetTempPath(), $"rtsp-test-{Guid.NewGuid()}");
        var mtxYml        = Path.Combine(credsDir, "mediamtx.yml");
        Directory.CreateDirectory(credsDir);

        _host = new LocalUiHost(
            port:       0,
            apiBaseUrl: _apiServer.Url!,
            getJwt:     () => "fake-jwt");
        await _host.StartAsync(default);

        // 4. POST /cameras
        using var http = new HttpClient { BaseAddress = new Uri(_host.BoundUrl!) };
        var body = new
        {
            rtsp_url = $"rtsp://127.0.0.1:{rtspPort}/live",
            label    = "Test Camera",
        };
        var resp = await http.PostAsJsonAsync("/cameras", body);

        // 5. Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("id").GetString().Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        json.GetProperty("stream_path").GetString().Should().Be("muni-test/cam-x");

        rtspListener.Stop();
        await rtspServerTask;
    }

    /// <summary>
    /// When the RTSP server replies 401 Unauthorized, POST /cameras should return 422
    /// with an error message indicating credentials were rejected.
    /// </summary>
    [Fact]
    public async Task PostCamera_AuthRequiredRtsp_Returns422()
    {
        // 1. Fake RTSP server that replies 401 Unauthorized
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var listenTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buf = new byte[1024];
                await stream.ReadAsync(buf);
                var resp = Encoding.ASCII.GetBytes("RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\n\r\n");
                await stream.WriteAsync(resp);
            }
            catch { }
        });

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 2. Fake cameras API (WireMock) — should not be called, but required by LocalUiHost
        _apiServer = WireMockServer.Start();

        // 3. LocalUiHost wired with all dependencies
        _host = new LocalUiHost(
            port:       0,
            apiBaseUrl: _apiServer.Url!,
            getJwt:     () => "fake-jwt");
        await _host.StartAsync(default);

        // 4. POST /cameras with rtsp_url pointing at the 401-responding server
        using var http = new HttpClient { BaseAddress = new Uri(_host.BoundUrl!) };
        var body = new { rtsp_url = $"rtsp://127.0.0.1:{port}/live", label = "Auth Required Camera" };
        var resp = await http.PostAsJsonAsync("/cameras", body);

        // 5. Assert 422 with credentials-rejected message
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("error").GetString().Should()
            .Contain("RTSP credentials rejected");

        listener.Stop();
        await listenTask;
    }

    /// <summary>
    /// When no RTSP server is listening, POST /cameras should return 422.
    /// </summary>
    [Fact]
    public async Task PostCamera_UnreachableRtsp_Returns422()
    {
        // Find a free port (bind then stop immediately).
        var tmp = new TcpListener(IPAddress.Loopback, 0);
        tmp.Start();
        int deadPort = ((IPEndPoint)tmp.LocalEndpoint).Port;
        tmp.Stop();

        _apiServer = WireMockServer.Start(); // needed for constructor but won't be called

        var credsDir = Path.Combine(Path.GetTempPath(), $"rtsp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(credsDir);

        _host = new LocalUiHost(
            port:       0,
            apiBaseUrl: _apiServer.Url!,
            getJwt:     () => "fake-jwt");
        await _host.StartAsync(default);

        using var http = new HttpClient { BaseAddress = new Uri(_host.BoundUrl!) };
        var body = new { rtsp_url = $"rtsp://127.0.0.1:{deadPort}/live", label = "Dead Camera" };
        var resp = await http.PostAsJsonAsync("/cameras", body);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("error").GetString().Should()
            .Contain("RTSP host unreachable");
    }
}
