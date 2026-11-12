using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Auth.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Api.Services;

/// <summary>Налаштування JWT (секція "Jwt"): ті самі Issuer / Audience / SecretKey валідують усі мікросервіси.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretKey { get; set; } = "";              // >= 32 символи (HMAC-SHA256); у production — з секретів
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public interface IJwtTokenService
{
    /// <summary>Access token: 15 хв, claims sub (userId), unique_name, email, role (кожна роль окремо).</summary>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    /// <summary>Криптографічно випадковий refresh token (повертається клієнту) і його SHA-256 хеш (зберігається в БД).</summary>
    (string Token, string Hash) GenerateRefreshToken();

    /// <summary>Перевірка підпису, issuer, audience і терміну дії; null — токен недійсний.</summary>
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);

    string Hash(string token);
}

public sealed class JwtTokenService(IOptions<JwtSettings> options) : IJwtTokenService
{
    private readonly JwtSettings _settings = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    private SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(_settings.SecretKey));

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id,
            [JwtRegisteredClaimNames.UniqueName] = user.UserName!,
            [JwtRegisteredClaimNames.Email] = user.Email ?? "",
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            ["role"] = roles.ToArray()                        // масив -> кілька значень claim "role"
        };
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            Expires = expires,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)
        });
        return (token, expires);
    }

    public (string Token, string Hash) GenerateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (token, Hash(token));
    }

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _settings.Issuer,
            ValidAudience = _settings.Audience,
            IssuerSigningKey = Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        });
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }
}
