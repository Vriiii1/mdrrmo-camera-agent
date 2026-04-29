using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Config;
using MdrrmoCameraAgent.LocalUi;
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

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    public MinimumViableAgent(
        ProvisioningBundle bundle,
        string hostname,
        TimeSpan? heartbeatInterval = null)
    {
        _bundle            = bundle;
        _hostname          = hostname;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Foreground loop: bootstrap once, then send a heartbeat every
    /// <see cref="_heartbeatInterval"/> until the supplied token is cancelled.
    /// JwtCache handles its own T-5 min refresh automatically.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();

        var (_, jwtCache, hbClient, sbHttp, hbHttp) = await BootstrapAsync(ct);

        var ui = new LocalUiHost(
            port:       8787,
            apiBaseUrl: _bundle.ApiBaseUrl!,
            getJwt:     () => jwtCache.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult());
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
                    await hbClient.SendAsync(jwt, Array.Empty<HeartbeatCamera>(), ct);
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
            await ui.StopAsync(CancellationToken.None);
            sbHttp.Dispose();
            hbHttp.Dispose();
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
            creds = await enroll.EnrollAsync(_bundle.EnrollmentToken!, _hostname, "0.0.1", ct);
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
