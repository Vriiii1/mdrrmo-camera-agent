using System.Diagnostics;
using System.Net.Http.Json;

namespace MdrrmoCameraAgent.Mtx;

public sealed class MediaMtxRunner : IAsyncDisposable
{
    private readonly string _exe;
    private readonly string _config;
    private readonly string[] _args;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly HttpClient _controlApi;

    private Process? _proc;
    private CancellationTokenSource? _watchdogCts;
    private Task? _watchdogTask;

    public int CrashCount { get; private set; }
    public DateTimeOffset? LastCrashAt { get; private set; }
    public bool IsRunning => _proc is { HasExited: false };

    public MediaMtxRunner(
        string exePath,
        string configPath,
        string[]? args = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        HttpMessageHandler? controlApiHandler = null,
        string controlApiBase = "http://127.0.0.1:9997")
    {
        _exe            = exePath;
        _config         = configPath;
        _args           = args ?? new[] { configPath };
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(5);
        _maxBackoff     = maxBackoff     ?? TimeSpan.FromMinutes(5);
        _controlApi     = controlApiHandler is null
            ? new HttpClient { BaseAddress = new Uri(controlApiBase) }
            : new HttpClient(controlApiHandler) { BaseAddress = new Uri(controlApiBase) };
    }

    public async Task StartAsync()
    {
        if (_watchdogTask is not null) return;

        _watchdogCts = new CancellationTokenSource();
        // Signal when the first child has been spawned so StartAsync can return
        // only after IsRunning is reliable.
        var firstSpawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _watchdogTask = Task.Run(() => SuperviseAsync(_watchdogCts.Token, firstSpawn));
        await firstSpawn.Task;
    }

    public async Task StopAsync()
    {
        _watchdogCts?.Cancel();
        if (_watchdogTask is not null)
        {
            try { await _watchdogTask; } catch (OperationCanceledException) { }
            _watchdogTask = null;
        }
        await KillChildAsync();
        _watchdogCts?.Dispose();
        _watchdogCts = null;
    }

    public async Task ReloadAsync(IEnumerable<CameraEntry> cameras, CancellationToken ct = default)
    {
        foreach (var c in cameras)
        {
            using var resp = await _controlApi.PostAsJsonAsync(
                $"/v3/config/paths/replace/{Uri.EscapeDataString(c.StreamPath)}",
                new
                {
                    source            = c.RtspUrl,
                    sourceOnDemand    = false,
                    runOnReady        = $"ffmpeg -hide_banner -loglevel warning -i rtsp://127.0.0.1:8554/$MTX_PATH -c copy -f whip \"{c.WhipUrl}\"",
                    runOnReadyRestart = true,
                },
                ct);
            resp.EnsureSuccessStatusCode();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _controlApi.Dispose();
    }

    private async Task SuperviseAsync(CancellationToken ct, TaskCompletionSource? firstSpawn = null)
    {
        var backoff = _initialBackoff;
        while (!ct.IsCancellationRequested)
        {
            SpawnChild();
            firstSpawn?.TrySetResult();
            firstSpawn = null; // only signal once

            try { await _proc!.WaitForExitAsync(ct); }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;

            CrashCount++;
            LastCrashAt = DateTimeOffset.UtcNow;
            Console.Error.WriteLine($"[mediamtx] child exited (code {_proc!.ExitCode}); crash #{CrashCount}; sleeping {backoff} before respawn");
            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { return; }

            backoff = backoff < _maxBackoff
                ? TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _maxBackoff.Ticks))
                : _maxBackoff;
        }
    }

    private void SpawnChild()
    {
        var psi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        foreach (var a in _args) psi.ArgumentList.Add(a);
        _proc = Process.Start(psi)!;
    }

    private async Task KillChildAsync()
    {
        if (_proc is null) return;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(entireProcessTree: true);
                await _proc.WaitForExitAsync();
            }
        }
        finally
        {
            _proc.Dispose();
            _proc = null;
        }
    }
}
