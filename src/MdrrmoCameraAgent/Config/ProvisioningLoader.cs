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
        if (string.IsNullOrWhiteSpace(bundle.HubBaseUrl))
            throw new InvalidDataException("provisioning JSON missing hub_base_url");
        if (string.IsNullOrWhiteSpace(bundle.SupabaseUrl))
            throw new InvalidDataException("provisioning JSON missing supabase_url");
        if (string.IsNullOrWhiteSpace(bundle.SupabaseAnonKey))
            throw new InvalidDataException("provisioning JSON missing supabase_anon_key");

        return bundle;
    }
}
