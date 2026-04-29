using System.Diagnostics;

namespace MdrrmoCameraAgent.Lifecycle;

/// <summary>
/// Default implementation: launches a real OS process and waits for exit.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task RunAsync(string exe, string[] args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardError = true,  // capture stderr for error messages
                UseShellExecute = false,
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        if (process.Start() is false) throw new InvalidOperationException($"Failed to start process '{exe}'.");

        // Read stderr concurrently to avoid deadlock on full pipe buffer
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Kill the child so Windows SCM is not left with a partial operation
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Process '{exe}' exited with code {process.ExitCode}." +
                (string.IsNullOrWhiteSpace(stderr) ? "" : $" Stderr: {stderr.Trim()}"));
    }
}
