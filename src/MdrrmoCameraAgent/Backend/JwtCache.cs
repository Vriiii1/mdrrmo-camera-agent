using System.IdentityModel.Tokens.Jwt;

namespace MdrrmoCameraAgent.Backend;

public sealed record JwtClaims(string AgentId, string MunicipalityId, string UserRole)
{
    public static JwtClaims Decode(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        string Read(string name) =>
            token.Claims.FirstOrDefault(c => c.Type == name)?.Value
            ?? throw new InvalidOperationException($"JWT missing required claim '{name}'");
        return new JwtClaims(
            AgentId:        Read("agent_id"),
            MunicipalityId: Read("municipality_id"),
            UserRole:       Read("user_role"));
    }
}

public sealed class JwtCache(Func<CancellationToken, Task<string>> mint)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private JwtClaims? _claims;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await EnsureFreshAsync(ct);
        return _token!;
    }

    public async Task<JwtClaims> GetClaimsAsync(CancellationToken ct)
    {
        await EnsureFreshAsync(ct);
        return _claims!;
    }

    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var refreshAt = _expiresAt == DateTimeOffset.MinValue
                ? DateTimeOffset.MinValue
                : _expiresAt.AddMinutes(-5);
            if (_token is not null && DateTimeOffset.UtcNow < refreshAt) return;

            _token     = await mint(ct);
            _claims    = JwtClaims.Decode(_token);
            _expiresAt = ReadExp(_token);
            Console.WriteLine($"[jwt-diag] minted exp={_expiresAt:o} now={DateTimeOffset.UtcNow:o} delta={(_expiresAt - DateTimeOffset.UtcNow).TotalMinutes:F1}min");

            if (_claims.UserRole != "camera_agent")
                throw new InvalidOperationException(
                    $"camera_agent JWT expected, got user_role='{_claims.UserRole}'");
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset ReadExp(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        return token.ValidTo == default ? DateTimeOffset.UtcNow.AddHours(1) : token.ValidTo;
    }
}
