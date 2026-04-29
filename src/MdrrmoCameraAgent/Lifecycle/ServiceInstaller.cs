namespace MdrrmoCameraAgent.Lifecycle;

/// <summary>
/// Installs and uninstalls the Windows service via WinSW.
/// </summary>
public sealed class ServiceInstaller(IProcessRunner runner)
{
    /// <summary>
    /// Runs <c>winsw install</c> then <c>winsw start</c> to register and immediately
    /// start the Windows service described by <paramref name="serviceXml"/>.
    /// </summary>
    public async Task InstallAsync(string winswExe, string serviceXml, CancellationToken ct)
    {
        await runner.RunAsync(winswExe, new[] { "install", serviceXml }, ct);
        await runner.RunAsync(winswExe, new[] { "start",   serviceXml }, ct);
    }

    /// <summary>
    /// Runs <c>winsw stop</c> then <c>winsw uninstall</c> to remove the Windows service.
    /// winsw v3 requires the service to be stopped before it can be uninstalled.
    /// </summary>
    public async Task UninstallAsync(string winswExe, string serviceXml, CancellationToken ct)
    {
        await runner.RunAsync(winswExe, new[] { "stop",      serviceXml }, ct);
        await runner.RunAsync(winswExe, new[] { "uninstall", serviceXml }, ct);
    }
}
