using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Config;

public sealed record ProvisioningBundle(
    [property: JsonPropertyName("enrollment_token")] string? EnrollmentToken,
    [property: JsonPropertyName("hub_base_url")]     string? HubBaseUrl,
    [property: JsonPropertyName("jwks_url")]         string? JwksUrl,
    [property: JsonPropertyName("api_base_url")]     string? ApiBaseUrl,
    [property: JsonPropertyName("supabase_url")]     string? SupabaseUrl,
    [property: JsonPropertyName("supabase_anon_key")]string? SupabaseAnonKey);
