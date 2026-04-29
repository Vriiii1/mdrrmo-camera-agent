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
[Collection("SequentialIntegration")]
public class AddCameraEndpointTests : IAsyncDisposable
{
    private LocalUiHost? _host;
    private WireMockServer? _apiServer;
    private readonly string _tmpRoot;

    public AddCameraEndpointTests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), $"agent-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tmpRoot);
        // Redirect AppPaths so tests never write to ProgramData and cannot
        // hit UnauthorizedAccessException from a prior ApplyHardenedDacl call.
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", _tmpRoot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.StopAsync(default);
        _apiServer?.Stop();
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
        try { Directory.Delete(_tmpRoot, recursive: true); } catch { }
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
    /// When the camera registry is at the cap, POST /cameras should return 429.
    /// </summary>
    [Fact]
    public async Task PostCameras_Refuses_WhenMaxCamerasReached()
    {
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_MAX_CAMERAS", "2");
        var camerasFile = MdrrmoCameraAgent.AppPaths.CamerasFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(camerasFile)!);
            File.WriteAllText(camerasFile, """
                [{"Id":"a","StreamPath":"x/a","RtspUrl":"rtsp://1/a","WhipUrl":null},
                 {"Id":"b","StreamPath":"x/b","RtspUrl":"rtsp://1/b","WhipUrl":null}]
                """);

            _apiServer = WireMockServer.Start();
            _host = new LocalUiHost(
                port:       0,
                apiBaseUrl: _apiServer.Url!,
                getJwt:     () => "fake-jwt");
            await _host.StartAsync(default);

            using var http = new HttpClient { BaseAddress = new Uri(_host.BoundUrl!) };
            var resp = await http.PostAsJsonAsync("/cameras", new { rtsp_url = "rtsp://127.0.0.1:9/live" });

            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
            var body = await resp.Content.ReadAsStringAsync();
            body.Should().Contain("max cameras reached");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDRRMO_AGENT_MAX_CAMERAS", null);
            try { File.Delete(camerasFile); } catch { }
        }
    }

    /// <summary>
    /// After a successful insert, the onCamerasChanged callback must fire exactly once.
    /// </summary>
    [Fact]
    public async Task PostCameras_TriggersOnCamerasChanged_AfterSuccessfulInsert()
    {
        // 1. Fake RTSP server (200 OK)
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
                    .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(201)
                    .WithBodyAsJson(new
                    {
                        id          = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                        stream_path = "muni-test/cam-cb",
                    }));

        // 3. Callback counter
        int callCount = 0;

        _host = new LocalUiHost(
            port:             0,
            apiBaseUrl:       _apiServer.Url!,
            getJwt:           () => "fake-jwt",
            onCamerasChanged: () => { callCount++; return Task.CompletedTask; });
        await _host.StartAsync(default);

        // 4. POST /cameras
        using var http = new HttpClient { BaseAddress = new Uri(_host.BoundUrl!) };
        var resp = await http.PostAsJsonAsync("/cameras",
            new { rtsp_url = $"rtsp://127.0.0.1:{rtspPort}/live", label = "CB Camera" });

        // 5. Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        callCount.Should().Be(1);

        rtspListener.Stop();
        await rtspServerTask;
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
