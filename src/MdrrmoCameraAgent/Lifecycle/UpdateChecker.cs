using System.Runtime.Versioning;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace MdrrmoCameraAgent.Lifecycle;

/// <summary>
/// Polls for Velopack releases on a GitHub Pages feed and applies any available update.
/// ApplyUpdatesAndRestart exits and relaunches the process — this is intentional.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateChecker(string feedUrl)
{
    private const string DefaultFeedUrl = "https://Vriiii1.github.io/mdrrmo-camera-agent/";

    /// <summary>
    /// Creates an <see cref="UpdateChecker"/> pointed at the production GitHub Pages feed.
    /// </summary>
    public UpdateChecker() : this(DefaultFeedUrl) { }

    public async Task CheckAndApplyAsync(CancellationToken ct)
    {
        try
        {
            var mgr  = new UpdateManager(new SimpleWebSource(feedUrl));
            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return;
            await mgr.DownloadUpdatesAsync(info, cancelToken: ct);
            mgr.ApplyUpdatesAndRestart(info);
        }
        catch (NotInstalledException)
        {
            // Inno-installed agents have no Velopack metadata, so
            // CheckForUpdatesAsync throws NotInstalledException. Auto-update
            // is a Velopack-only feature; Inno boxes upgrade by re-running
            // the installer. Silently no-op.
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("VelopackLocator", StringComparison.Ordinal))
        {
            // Same root cause as NotInstalledException, surfaced as
            // InvalidOperationException("No VelopackLocator has been set...")
            // in environments where Velopack hasn't been initialized via
            // VelopackApp.Build() (observed in unit-test runs and on Inno
            // installs that never bootstrap Velopack at all). Silently no-op
            // for the same reason. The `when` filter keeps this catch narrow:
            // unrelated InvalidOperationExceptions (e.g. from
            // ApplyUpdatesAndRestart or future API surfaces) still propagate.
        }
    }
}
