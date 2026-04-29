using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace MdrrmoCameraAgent.LocalUi;

public sealed class LocalUiHost
{
    private readonly int _requestedPort;
    private WebApplication? _app;
    public string? BoundUrl { get; private set; }

    public LocalUiHost(int port = 8787) => _requestedPort = port;

    public async Task StartAsync(CancellationToken ct)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, _requestedPort));
        b.WebHost.SuppressStatusMessages(true);
        _app = b.Build();
        _app.MapGet("/health", () => Results.Ok(new { ok = true }));
        await _app.StartAsync(ct);
        BoundUrl = _app.Urls.First();
    }

    public Task StopAsync(CancellationToken ct) =>
        _app?.StopAsync(ct) ?? Task.CompletedTask;
}
