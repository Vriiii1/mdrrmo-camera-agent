using System.Diagnostics;

namespace MdrrmoCameraAgent.Lifecycle;

/// <summary>
/// Default implementation: launches a real OS process and waits for exit.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = false,
            RedirectStandardError  = false,
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exe}");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Process '{exe}' exited with code {process.ExitCode}.");
    }
}
