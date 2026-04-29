using FluentAssertions;
using MdrrmoCameraAgent.LocalUi;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.Versioning;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MdrrmoCameraAgent.Tests;

/// <summary>
/// Marks a collection of tests that must not run in parallel with one another.
/// Any test class that mutates the process-wide MDRRMO_AGENT_ROOT env var should
/// join this collection to prevent flaky failures when AppPathsTests reads the
/// default root concurrently.
/// </summary>
[CollectionDefinition("SequentialIntegration", DisableParallelization = true)]
public class SequentialIntegrationCollection { }

/// <summary>
/// Wave 5 wave-end integration test. Exercises the full Generic-RTSP add-camera flow:
/// POST /cameras → RtspProbe → DpapiVault → CamerasApiClient → MediaMtxConfigWriter →
/// cameras.json registry, with tenant-safety invariant (no municipality_id in request body).
/// </summary>
[Collection("SequentialIntegration")]
[SupportedOSPlatform("windows")]
public class Wave5IntegrationTests : IAsyncLifetime
{
    private string          _tempRoot = string.Empty;
    private WireMockServer? _api;
    private TcpListener?    _fakeRtsp;
    private Task?           _rtspLoop;
    private LocalUiHost?    _ui;

    public async Task InitializeAsync()
    {
        // 1. Temp agent root so tests don't touch C:\ProgramData
        _tempRoot = Path.Combine(Path.GetTempPath(), "wave5-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", _tempRoot);
        AppPaths.EnsureDirectoriesExist();

        // Also ensure the cameras.creds.dpapi directory exists
        // (AppPaths.CameraCreds is used as a directory by AddCameraEndpoint)
        Directory.CreateDirectory(AppPaths.CameraCreds);

        // 2. WireMock stub for cameras API
        _api = WireMockServer.Start();
        _api.Given(Request.Create()
                .WithPath("/api/v1/agents/cameras/").UsingPost()
                .WithHeader("Authorization", "Bearer fake-jwt"))
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new {
                id          = "11111111-1111-1111-1111-111111111111",
                stream_path = "muni-infanta/cam-11111111",
            }));

        // 3. Fake RTSP server — accepts TCP, replies "RTSP/1.0 200 OK\r\n\r\n"
        _fakeRtsp = new TcpListener(IPAddress.Loopback, 0);
        _fakeRtsp.Start();
        _rtspLoop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await _fakeRtsp.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    var buf = new byte[1024];
                    await stream.ReadAsync(buf);
                    var resp = System.Text.Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n");
                    await stream.WriteAsync(resp);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException)         { }
        });

        // 4. Start LocalUiHost with stubbed backends
        _ui = new LocalUiHost(port: 0, apiBaseUrl: _api.Url!, getJwt: () => "fake-jwt");
        await _ui.StartAsync(default);
    }

    public async Task DisposeAsync()
    {
        if (_ui is not null)
            await _ui.StopAsync(default);

        _fakeRtsp?.Stop();

        if (_rtspLoop is not null)
        {
            try { await _rtspLoop; }
            catch (ObjectDisposedException) { }
            catch (SocketException)         { }
        }

        _api?.Stop();

        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        Environment.SetEnvironmentVariable("MDRRMO_AGENT_ROOT", null);
    }

    [Fact]
    public async Task AddCamera_GenericRtsp_FullFlowSucceeds()
    {
        var port = ((IPEndPoint)_fakeRtsp!.LocalEndpoint).Port;
        using var http = new HttpClient { BaseAddress = new Uri(_ui!.BoundUrl!) };

        var resp = await http.PostAsJsonAsync("/cameras", new {
            rtsp_url = $"rtsp://127.0.0.1:{port}/test",
            label    = "City Hall front gate",
        });

        resp.IsSuccessStatusCode.Should().BeTrue(
            $"response was {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");

        // 1. Cameras API was hit
        _api!.LogEntries.Any(e =>
            e.RequestMessage?.AbsolutePath == "/api/v1/agents/cameras/")
            .Should().BeTrue("the cameras API endpoint should have been called");

        // 2. RTSP creds were stored in DPAPI.
        //    AddCameraEndpoint writes the raw RTSP URL as a machine-scope DPAPI blob to:
        //      Path.Combine(AppPaths.CameraCreds, $"cam-{cameraId}.dpapi")
        //    AppPaths.CameraCreds is a directory (not a file), so we verify at least one
        //    .dpapi file was created inside it — and that none of them expose the plaintext URL.
        Directory.Exists(AppPaths.CameraCreds).Should().BeTrue(
            "the camera creds directory should have been created by DpapiVault.WriteString");
        var credsFiles = Directory.GetFiles(AppPaths.CameraCreds, "cam-*.dpapi");
        credsFiles.Should().HaveCountGreaterThan(0,
            "DpapiVault.WriteString should have written at least one cam-<id>.dpapi file");

        // The stored file should be opaque (DPAPI-encrypted, not plaintext RTSP URL)
        var rawBytes = File.ReadAllBytes(credsFiles[0]);
        System.Text.Encoding.UTF8.GetString(rawBytes).Should()
            .NotContain("rtsp://", because: "DPAPI blobs must not contain the plaintext RTSP URL");

        // 3. mediamtx.yml was regenerated. At enrollment time the hub_base_url is not yet
        //    known, so WhipUrl is empty and the camera is not publishable yet. The yml should
        //    contain only a registration-only stub with `paths: {}`. The stream path will
        //    appear once hub_base_url is provisioned and WhipUrl is populated.
        File.Exists(AppPaths.MediaMtxYml).Should().BeTrue("mediamtx.yml was not written");
        var mtxYml = File.ReadAllText(AppPaths.MediaMtxYml);
        mtxYml.Should().Contain("paths: {}", "enrollment-only cameras must yield a valid stub yml");
        mtxYml.Should().NotContain("runOnReady", "no publish loop until hub_base_url is set");

        // 4. Camera registry (cameras.json) was written
        File.Exists(AppPaths.CamerasFile).Should().BeTrue("cameras.json registry was not written");
        File.ReadAllText(AppPaths.CamerasFile).Should().Contain("muni-infanta/cam-11111111");

        // 5. Body sent to cameras API did NOT contain municipality_id
        var lastReq = _api.LogEntries
            .First(e => e.RequestMessage?.AbsolutePath == "/api/v1/agents/cameras/")
            .RequestMessage?.Body ?? string.Empty;
        lastReq.Should().NotContain("municipality_id",
            because: "server derives tenant identity from JWT — client must never assert it");
    }
}
