namespace MdrrmoCameraAgent.Mtx;

using MdrrmoCameraAgent.Backend;

public sealed class PublishTokenRefresher(
    Func<string, string, CancellationToken, Task<PublishTokenResult>> getToken,
    Func<CancellationToken, Task<string>> getJwt,
    Action<string, string> onUrlChanged,
    TimeSpan? safetyMargin = null,
    TimeSpan? minSleep = null)
{
    private readonly TimeSpan _safetyMargin = safetyMargin ?? TimeSpan.FromSeconds(60);
    private readonly TimeSpan _minSleep     = minSleep     ?? TimeSpan.FromSeconds(5);

    public async Task<DateTimeOffset> RefreshOnceAsync(string cameraId, CancellationToken ct)
    {
        var jwt    = await getJwt(ct);
        var result = await getToken(jwt, cameraId, ct);
        onUrlChanged(cameraId, result.Url);
        return result.ExpiresAt;
    }

    public Task RunAsync(IEnumerable<string> cameraIds, CancellationToken ct)
        => Task.WhenAll(cameraIds.Select(id => RefreshLoopAsync(id, ct)));

    private async Task RefreshLoopAsync(string cameraId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            DateTimeOffset expiresAt;
            try { expiresAt = await RefreshOnceAsync(cameraId, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[publish-token {cameraId}] {ex.GetType().Name}: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            var sleep = expiresAt - DateTimeOffset.UtcNow - _safetyMargin;
            if (sleep < _minSleep) sleep = _minSleep;

            try { await Task.Delay(sleep, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
