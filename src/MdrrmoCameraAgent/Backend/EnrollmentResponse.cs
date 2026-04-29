using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

internal sealed record EnrollmentRequest(
    [property: JsonPropertyName("enrollment_token")] string EnrollmentToken,
    [property: JsonPropertyName("hostname")]          string Hostname,
    [property: JsonPropertyName("version")]           string Version);

public sealed record EnrollmentResponse(
    [property: JsonPropertyName("agent_id")]        string AgentId,
    [property: JsonPropertyName("agent_secret")]    string AgentSecret,
    [property: JsonPropertyName("hub_base_url")]    string? HubBaseUrl,
    [property: JsonPropertyName("jwks_url")]        string? JwksUrl,
    [property: JsonPropertyName("auth_bridge_url")] string? AuthBridgeUrl);

public sealed class EnrollmentException(string msg) : Exception(msg);
