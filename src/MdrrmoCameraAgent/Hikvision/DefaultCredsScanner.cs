namespace MdrrmoCameraAgent.Hikvision;

/// <summary>
/// Tries known PH-default Hikvision NVR credentials and returns the first working pair.
/// Surfaces a warning to the operator — does not block enrollment.
/// </summary>
public static class DefaultCredsScanner
{
    public static IReadOnlyList<(string User, string Pass)> KnownDefaults { get; } =
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
        foreach (var (user, pass) in KnownDefaults)
        {
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(nvrBaseUrl) };
                var client = new IsapiClient(http, user, pass);
                await client.ListChannelsAsync(ct);
                return (user, pass);
            }
            catch (HttpRequestException) { /* 401 or connection error — try next */ }
        }
        return null;
    }
}
