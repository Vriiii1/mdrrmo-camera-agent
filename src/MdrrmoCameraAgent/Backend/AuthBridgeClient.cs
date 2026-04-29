using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdrrmoCameraAgent.Backend;

public sealed class AuthBridgeClient(HttpClient http, string anonKey)
{
    public async Task<string> MintJwtAsync(string agentId, string agentSecret, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/rpc/auth_bridge_agent")
        {
            Content = JsonContent.Create(new AuthBridgeRequest(agentId, agentSecret))
        };
        req.Headers.Add("apikey", anonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new AuthBridgeException($"auth_bridge_agent RPC failed: HTTP {(int)resp.StatusCode} — {body}");

        // PostgREST returns the function's TEXT result wrapped in JSON quotes.
        var jwt = JsonSerializer.Deserialize<string>(body);
        if (string.IsNullOrEmpty(jwt))
            throw new AuthBridgeException("auth_bridge_agent returned empty JWT");
        return jwt;
    }
}

internal sealed record AuthBridgeRequest(
    [property: JsonPropertyName("p_agent_id")]     string PAgentId,
    [property: JsonPropertyName("p_agent_secret")] string PAgentSecret);

public sealed class AuthBridgeException(string msg) : Exception(msg);
