using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ProductManager.Api.Auth;

/// <summary>Issues (and describes) the single-user bearer token.</summary>
public sealed class TokenIssuer(IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    /// <summary>Constant-time password check against the configured single-user password.</summary>
    public bool PasswordMatches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(_options.Password))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(_options.Password));
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.TokenLifetimeMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, _options.Subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
