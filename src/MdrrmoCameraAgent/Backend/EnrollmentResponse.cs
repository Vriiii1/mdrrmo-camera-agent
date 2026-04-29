using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

public sealed record EnrollmentResponse(
    [property: JsonPropertyName("agent_id")]        string AgentId,
    [property: JsonPropertyName("agent_secret")]    string AgentSecret,
    [property: JsonPropertyName("hub_base_url")]    string? HubBaseUrl,
    [property: JsonPropertyName("jwks_url")]        string? JwksUrl,
    [property: JsonPropertyName("auth_bridge_url")] string? AuthBridgeUrl);

public sealed class EnrollmentException(string msg) : Exception(msg);
