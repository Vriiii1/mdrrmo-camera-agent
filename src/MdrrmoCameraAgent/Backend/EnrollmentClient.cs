using System.Net.Http.Json;

namespace MdrrmoCameraAgent.Backend;

public sealed class EnrollmentClient(HttpClient http)
{
    public async Task<EnrollmentResponse> EnrollAsync(
        string enrollmentToken, string hostname, string version, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync(
            "/api/v1/agents/enrollment",
            new EnrollmentRequest(enrollmentToken, hostname, version),
            ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new EnrollmentException(
                $"enrollment failed: HTTP {(int)resp.StatusCode} — {body}");
        }

        return await resp.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken: ct)
               ?? throw new EnrollmentException("enrollment response was null");
    }
}
