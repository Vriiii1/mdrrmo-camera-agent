using System.Text.Json;

namespace MdrrmoCameraAgent.Config;

public static class ProvisioningLoader
{
    public static ProvisioningBundle LoadFromFile(string path)
    {
        var json   = File.ReadAllText(path);
        var bundle = JsonSerializer.Deserialize<ProvisioningBundle>(json)
                     ?? throw new InvalidDataException("provisioning JSON deserialized to null");

        if (string.IsNullOrWhiteSpace(bundle.EnrollmentToken))
            throw new InvalidDataException("provisioning JSON missing enrollment_token");
        if (string.IsNullOrWhiteSpace(bundle.SupabaseUrl))
            throw new InvalidDataException("provisioning JSON missing supabase_url");
        if (string.IsNullOrWhiteSpace(bundle.SupabaseAnonKey))
            throw new InvalidDataException("provisioning JSON missing supabase_anon_key");
        // hub_base_url, jwks_url, and api_base_url are intentionally optional — they're
        // only populated when the CCTV Hub service is deployed (CCTV_HUB_BASE_URL /
        // CCTV_HUB_JWKS_URL on the dashboard). Until then the dashboard emits them as
        // JSON null, which the agent ignores gracefully (registration-only mode).

        return bundle;
    }
}
