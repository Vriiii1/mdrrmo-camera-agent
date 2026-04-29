using System.Runtime.Versioning;
using Velopack;
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
        var mgr  = new UpdateManager(new SimpleWebSource(feedUrl));
        var info = await mgr.CheckForUpdatesAsync();
        if (info is null) return;
        await mgr.DownloadUpdatesAsync(info, cancelToken: ct);
        mgr.ApplyUpdatesAndRestart(info);
    }
}
