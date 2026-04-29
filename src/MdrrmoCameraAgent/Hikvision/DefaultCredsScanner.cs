namespace MdrrmoCameraAgent.Hikvision;

/// <summary>
/// Tries known PH-default Hikvision NVR credentials and returns the first working pair.
/// Surfaces a warning to the operator — does not block enrollment.
/// </summary>
public static class DefaultCredsScanner
{
    private static readonly IReadOnlyList<(string User, string Pass)> _knownDefaults =
    [
        ("admin", "12345"),
        ("admin", "Admin12345"),
        ("admin", "admin"),
    ];

    /// <returns>The first working (user, pass) pair, or null if none matched.</returns>
    public static async Task<(string User, string Pass)?> ScanAsync(
        string nvrBaseUrl,
        CancellationToken ct)
    {
        foreach (var (user, pass) in _knownDefaults)
        {
            try
            {
                var http = new HttpClient { BaseAddress = new Uri(nvrBaseUrl) };
                var client = new IsapiClient(http, user, pass);
                var channels = await client.ListChannelsAsync(ct);
                if (channels.Count >= 0)   // any response (even 0 channels) means auth worked
                    return (user, pass);
            }
            catch (HttpRequestException) { /* 401 or connection error — try next */ }
        }
        return null;
    }
}
