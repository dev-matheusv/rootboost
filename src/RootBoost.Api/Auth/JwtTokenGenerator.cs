using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RootBoost.Api.Auth;

/// <summary>Emite o JWT do Console. Portado do RootFlow, sem workspace por enquanto.</summary>
public sealed class JwtTokenGenerator
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public JwtTokenGenerator(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public GeneratedJwtToken Generate(string email, string name, string role)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(_options.ExpiresInMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, email),
            new(ClaimTypes.NameIdentifier, email),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(CreateSigningKey(_options.Key), SecurityAlgorithms.HmacSha256));

        return new GeneratedJwtToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public static SymmetricSecurityKey CreateSigningKey(string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            throw new InvalidOperationException("JWT signing key must be configured with at least 32 characters.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }
}

public sealed record GeneratedJwtToken(string Token, DateTime ExpiresAtUtc);
