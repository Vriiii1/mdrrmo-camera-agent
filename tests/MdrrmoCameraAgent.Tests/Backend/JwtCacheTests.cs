using FluentAssertions;
using MdrrmoCameraAgent.Backend;

namespace MdrrmoCameraAgent.Tests.Backend;

public class JwtCacheTests
{
    [Fact]
    public async Task GetToken_CallsMintOnce_WhenStillFresh()
    {
        int calls = 0;
        string Mint() { calls++; return MintTestJwt(TimeSpan.FromHours(1)); }

        var cache = new JwtCache(_ => Task.FromResult(Mint()));
        await cache.GetTokenAsync(default);
        await cache.GetTokenAsync(default);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetToken_RefreshesWhenLessThan5MinutesLeft()
    {
        int calls = 0;
        string Mint() { calls++; return MintTestJwt(TimeSpan.FromMinutes(4)); }

        var cache = new JwtCache(_ => Task.FromResult(Mint()));
        await cache.GetTokenAsync(default);
        await cache.GetTokenAsync(default);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetClaims_ReturnsTenantClaims()
    {
        string Mint() {
            var header  = "{\"alg\":\"none\"}";
            var payload = "{\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                         + ",\"agent_id\":\"aaa\",\"municipality_id\":\"infanta\",\"user_role\":\"camera_agent\"}";
            string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
                .TrimEnd('=').Replace('+','-').Replace('/','_');
            return $"{B64(header)}.{B64(payload)}.";
        }
        var cache = new JwtCache(_ => Task.FromResult(Mint()));
        var claims = await cache.GetClaimsAsync(default);
        claims.AgentId.Should().Be("aaa");
        claims.MunicipalityId.Should().Be("infanta");
        claims.UserRole.Should().Be("camera_agent");
    }

    // CONTRACT PIN — see supabase/migrations/164_auth_bridge_camera_agent.sql.
    // If the auth_bridge_agent RPC's claim shape ever changes, THIS TEST fails first.
    // Update this test AND JwtClaims.Decode AND RLS policy reads in lockstep.
    [Fact]
    public void Contract_JwtShape_MustMatchAuthBridgeAgentRpc()
    {
        var payload = "{\"role\":\"authenticated\","
                    + "\"user_role\":\"camera_agent\","
                    + "\"agent_id\":\"11111111-1111-1111-1111-111111111111\","
                    + "\"municipality_id\":\"infanta\","
                    + "\"province_id\":\"\","
                    + "\"is_super_admin\":false,"
                    + "\"sub\":\"agent:11111111-1111-1111-1111-111111111111\","
                    + "\"aud\":\"authenticated\","
                    + "\"iss\":\"mdrrmo-auth-bridge-agent\","
                    + "\"exp\":9999999999,\"iat\":1700000000}";
        string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+','-').Replace('/','_');
        var jwt = $"{B64("{\"alg\":\"none\"}")}.{B64(payload)}.";

        var claims = JwtClaims.Decode(jwt);
        claims.UserRole.Should().Be("camera_agent");
        claims.AgentId.Should().Be("11111111-1111-1111-1111-111111111111");
        claims.MunicipalityId.Should().Be("infanta");
    }

    private static string MintTestJwt(TimeSpan ttl)
    {
        var header  = "{\"alg\":\"none\"}";
        var payload = $"{{\"exp\":{DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds()},\"agent_id\":\"test\",\"municipality_id\":\"test-muni\",\"user_role\":\"camera_agent\"}}";
        string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+','-').Replace('/','_');
        return $"{B64(header)}.{B64(payload)}.";
    }
}
