using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Config;
using MdrrmoCameraAgent.LocalUi;
using MdrrmoCameraAgent.Mtx;
using MdrrmoCameraAgent.Storage;
using System.Runtime.Versioning;
using System.Text.Json;

namespace MdrrmoCameraAgent.Lifecycle;

[SupportedOSPlatform("windows")]
public sealed class MinimumViableAgent
{
    private readonly ProvisioningBundle _bundle;
    private readonly string _hostname;
    private readonly TimeSpan _heartbeatInterval;
    private readonly int _localUiPort;

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    public MinimumViableAgent(
        ProvisioningBundle bundle,
        string hostname,
        TimeSpan? heartbeatInterval = null,
        int localUiPort = 8787)
    {
        _bundle            = bundle;
        _hostname          = hostname;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _localUiPort       = localUiPort;
    }

    /// <summary>
    /// Foreground loop: bootstrap once, then send a heartbeat every
    /// <see cref="_heartbeatInterval"/> until the supplied token is cancelled.
    /// JwtCache handles its own T-5 min refresh automatically.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();
        Console.WriteLine($"[paths] runtime root: {Path.GetDirectoryName(AppPaths.RuntimeYml)}");
        Console.WriteLine($"[paths] logs dir   : {AppPaths.RuntimeLogsDir}");

        // Emit before BootstrapAsync so the log line is visible even if bootstrap
        // throws on a network blip (plan-check [M7]).
        if (string.IsNullOrEmpty(_bundle.HubBaseUrl))
        {
            Console.WriteLine("[mediamtx] hub_base_url not configured — publish loop disabled (registration-only mode)");
        }

        var (_, jwtCache, hbClient, sbHttp, hbHttp) = await BootstrapAsync(ct);

        // Per spec §7: treat the dashboard as source-of-truth for the camera list on
        // every start; fall back to local cache only on network failure.
        var jwt0 = await jwtCache.GetTokenAsync(ct);
        await TryRefreshCamerasFromServerAsync(hbHttp, jwt0, ct);

        // Set up publish stack (or skip if hub not configured).
        var publishHost = new PublishLoopHost(LoadEnrolledCamerasOrEmpty());
        PublishStatusProbe?      probe    = null;
        MediaMtxRunner?          runner   = null;
        CancellationTokenSource? probeCts = null;

        if (!string.IsNullOrEmpty(_bundle.HubBaseUrl))
        {
            var ymlPath = AppPaths.RuntimeYml;

            // Pre-flight ACL guard on a stale yml from a crashed run.
            var acl = YmlAclGuard.Check(ymlPath);
            if (!acl.Safe)
            {
                Console.Error.WriteLine($"[mediamtx] stale yml refused ({acl.Reason}); deleting and regenerating");
                try { File.Delete(ymlPath); } catch { /* best-effort */ }
            }

            MediaMtxConfigWriter.WriteToFile(ymlPath, publishHost.Cameras);

            runner = new MediaMtxRunner(
                exePath:       AppPaths.MediaMtxExe,
                configPath:    ymlPath,
                wipeOnDispose: true,
                logFilePath:   Path.Combine(AppPaths.RuntimeLogsDir, "mediamtx.log"));
            try
            {
                await runner.StartAsync();
                probe    = new PublishStatusProbe(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9997") });
                probeCts = new CancellationTokenSource();
                _ = Task.Run(() => RunProbeLoopAsync(probe, publishHost, probeCts.Token), probeCts.Token);
            }
            catch (Exception ex)
            {
                // Bundled mediamtx.exe is missing or corrupt. Per spec §2 Reliability:
                // log a single ERROR and continue heartbeating. Do NOT crash.
                Console.Error.WriteLine($"[mediamtx] failed to start: {ex.GetType().Name}: {ex.Message}");
                await runner.DisposeAsync();
                runner = null;
            }
        }

        var ui = new LocalUiHost(
            port:             _localUiPort,
            apiBaseUrl:       _bundle.ApiBaseUrl!,
            getJwt:           () => jwtCache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult(),
            onCamerasChanged: async () =>
            {
                // Fast-path reload: <2 s after POST /cameras succeeds.
                if (publishHost.ReplaceCameras(LoadEnrolledCamerasOrEmpty()) && runner is not null)
                {
                    MediaMtxConfigWriter.WriteToFile(AppPaths.RuntimeYml, publishHost.Cameras);
                    await runner.ReloadAsync(publishHost.Cameras, ct);
                }
            });
        await ui.StartAsync(ct);
        Console.WriteLine($"Local UI: {ui.BoundUrl}");

        var checker         = new UpdateChecker();
        var lastUpdateCheck = DateTime.MinValue;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // ── 6-hour update check ───────────────────────────────────────
                if (DateTime.UtcNow - lastUpdateCheck >= UpdateCheckInterval)
                {
                    try
                    {
                        await checker.CheckAndApplyAsync(ct);
                        // If we reach here, no update was available (ApplyUpdatesAndRestart
                        // would have exited the process if an update was applied).
                    }
                    catch (OperationCanceledException) { break; }
                    catch (OutOfMemoryException) { throw; }
                    catch (Exception ex)
                    {
                        // Update checks are fault-tolerant; a failed check must not crash the agent.
                        Console.Error.WriteLine($"[update-check] {ex.GetType().Name}: {ex.Message}");
                    }
                    lastUpdateCheck = DateTime.UtcNow;
                }

                // ── heartbeat ─────────────────────────────────────────────────
                try
                {
                    var jwt = await jwtCache.GetTokenAsync(ct);

                    // Per-tick reload (≤30 s). Picks up dashboard-side adds and any external
                    // edits to cameras.json that the on-demand callback didn't cover.
                    if (publishHost.ReplaceCameras(LoadEnrolledCamerasOrEmpty()) && runner is not null)
                    {
                        try
                        {
                            MediaMtxConfigWriter.WriteToFile(AppPaths.RuntimeYml, publishHost.Cameras);
                            await runner.ReloadAsync(publishHost.Cameras, ct);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[mediamtx] tick-reload failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    await hbClient.SendAsync(jwt, publishHost.SnapshotHeartbeatPayload(), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (OutOfMemoryException) { throw; }
                catch (Exception ex)
                {
                    // Heartbeat is fault-tolerant: log and keep looping.
                    // Network blips on PH LGU connections are routine;
                    // one failed beat must not crash the agent.
                    Console.Error.WriteLine($"[heartbeat] {ex.GetType().Name}: {ex.Message}");
                }

                try { await Task.Delay(_heartbeatInterval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            probeCts?.Cancel();
            if (runner is not null) await runner.DisposeAsync();
            probeCts?.Dispose();
            await ui.StopAsync(CancellationToken.None);
            sbHttp.Dispose();
            hbHttp.Dispose();
        }
    }

    private static async Task RunProbeLoopAsync(
        PublishStatusProbe probe, PublishLoopHost host, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var paths = host.Cameras.Select(c => c.StreamPath).ToList();
                var snap  = await probe.SampleAsync(paths, ct);
                host.RecordProbeResult(snap);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[probe] {ex.GetType().Name}: {ex.Message}");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static IEnumerable<CameraEntry> LoadEnrolledCamerasOrEmpty()
    {
        var path = AppPaths.CamerasFile;
        if (!File.Exists(path)) return Array.Empty<CameraEntry>();
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<CameraEntry>>(json);
            return list ?? (IEnumerable<CameraEntry>)Array.Empty<CameraEntry>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cameras] failed to read {path}: {ex.Message}");
            return Array.Empty<CameraEntry>();
        }
    }

    private static async Task TryRefreshCamerasFromServerAsync(
        HttpClient apiHttp, string jwt, CancellationToken ct)
    {
        try
        {
            var client = new CamerasApiClient(apiHttp);
            var server = await client.ListAsync(jwt, ct);
            File.WriteAllText(AppPaths.CamerasFile,
                JsonSerializer.Serialize(server, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[cameras] refreshed {server.Count} entries from dashboard");
        }
        catch (Exception ex)
        {
            // Network blip / dashboard down at boot — fall back to whatever's
            // already in cameras.json. Single WARN, do NOT crash. Per spec §7.
            Console.Error.WriteLine($"[cameras] WARN — refresh failed, using local cache: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(EnrollmentResponse Creds, JwtCache Jwt, HeartbeatClient Hb, HttpClient SbHttp, HttpClient HbHttp)>
        BootstrapAsync(CancellationToken ct)
    {
        // 1. Enroll if no creds on disk.
        EnrollmentResponse? creds = TryReadCreds();
        if (creds is null)
        {
            using var apiHttp = new HttpClient { BaseAddress = new Uri(_bundle.ApiBaseUrl!) };
            var enroll = new EnrollmentClient(apiHttp);
            creds = await enroll.EnrollAsync(_bundle.EnrollmentToken!, _hostname, ThisAssembly.Version, ct);
            WriteCreds(creds);
        }

        var sbHttp = new HttpClient { BaseAddress = new Uri(_bundle.SupabaseUrl!) };
        var hbHttp = new HttpClient { BaseAddress = new Uri(_bundle.ApiBaseUrl!) };
        try
        {
            // 2. Build the JWT cache (auto-refresh handled inside JwtCache).
            var auth     = new AuthBridgeClient(sbHttp, _bundle.SupabaseAnonKey!);
            var jwtCache = new JwtCache(c => auth.MintJwtAsync(creds.AgentId, creds.AgentSecret, c));

            // 3. Heartbeat client — long-lived HttpClient.
            var hbClient = new HeartbeatClient(hbHttp);

            return (creds, jwtCache, hbClient, sbHttp, hbHttp);
        }
        catch
        {
            sbHttp.Dispose();
            hbHttp.Dispose();
            throw;
        }
    }

    private static EnrollmentResponse? TryReadCreds()
    {
        if (!DpapiVault.Exists(AppPaths.CredsFile)) return null;
        var json = DpapiVault.ReadString(AppPaths.CredsFile);
        return json is null
            ? null
            : JsonSerializer.Deserialize<EnrollmentResponse>(json);
    }

    private static void WriteCreds(EnrollmentResponse creds) =>
        DpapiVault.WriteString(AppPaths.CredsFile, JsonSerializer.Serialize(creds));
}
