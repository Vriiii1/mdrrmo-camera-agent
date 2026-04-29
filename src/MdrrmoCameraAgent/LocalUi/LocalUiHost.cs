using MdrrmoCameraAgent.Backend;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Runtime.Versioning;

namespace MdrrmoCameraAgent.LocalUi;

[SupportedOSPlatform("windows")]
public sealed class LocalUiHost
{
    private readonly int _requestedPort;
    private readonly string? _apiBaseUrl;
    private readonly Func<string>? _getJwt;
    private readonly Func<Task>? _onCamerasChanged;
    private WebApplication? _app;
    public string? BoundUrl { get; private set; }

    /// <summary>
    /// Minimal constructor — no /cameras route. Used by existing T16 tests.
    /// </summary>
    public LocalUiHost(int port = 8787) => _requestedPort = port;

    /// <summary>
    /// Full constructor — wires /cameras route with the provided dependencies.
    /// Used by T19b integration test and production startup.
    /// </summary>
    public LocalUiHost(
        int port,
        string apiBaseUrl,
        Func<string> getJwt,
        Func<Task>? onCamerasChanged = null)
    {
        _requestedPort    = port;
        _apiBaseUrl       = apiBaseUrl;
        _getJwt           = getJwt;
        _onCamerasChanged = onCamerasChanged;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, _requestedPort));
        b.WebHost.SuppressStatusMessages(true);
        b.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        b.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        _app = b.Build();
        _app.MapGet("/health", () => Results.Ok(new { ok = true }));

        if (_apiBaseUrl is not null && _getJwt is not null)
        {
            var camerasApi = new CamerasApiClient(
                new HttpClient { BaseAddress = new Uri(_apiBaseUrl) });
            var cameraCredsDir  = AppPaths.CameraCreds;
            var mediaMtxYmlPath = AppPaths.MediaMtxYml;
            var endpoint = new AddCameraEndpoint(
                camerasApi,
                _getJwt,
                cameraCredsDir,
                mediaMtxYmlPath,
                _onCamerasChanged);
            _app.MapPost("/cameras", ctx => endpoint.HandleAsync(ctx));
            _app.MapPost("/cameras/hikvision", AddHikvisionEndpoint.HandleListAsync);
        }

        // Use the exe's own directory so wwwroot is found when running as a Windows service
        // (AppContext.BaseDirectory points to the temp extraction dir for single-file apps).
        var exeDir      = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var wwwrootPath = Path.Combine(exeDir, "LocalUi", "wwwroot");
        if (Directory.Exists(wwwrootPath))
        {
            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
                RequestPath  = "",
            });
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
                RequestPath  = "",
            });
        }

        await _app.StartAsync(ct);
        BoundUrl = _app.Urls.First();
    }

    public Task StopAsync(CancellationToken ct) =>
        _app?.StopAsync(ct) ?? Task.CompletedTask;
}
