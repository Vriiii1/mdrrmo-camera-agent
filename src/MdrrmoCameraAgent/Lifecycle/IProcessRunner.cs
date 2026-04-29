namespace MdrrmoCameraAgent.Lifecycle;

/// <summary>
/// Abstraction over launching an external process so callers are testable
/// without spawning real OS processes.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="exe"/> with <paramref name="args"/> and waits for it
    /// to exit. Throws <see cref="InvalidOperationException"/> if the exit code
    /// is non-zero.
    /// </summary>
    Task RunAsync(string exe, string[] args, CancellationToken ct);
}
