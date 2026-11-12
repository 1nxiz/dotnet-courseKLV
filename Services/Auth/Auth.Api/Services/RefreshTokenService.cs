using Auth.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auth.Api.Services;

/// <summary>
/// Життєвий цикл refresh token: видача, rotation (старий відкликається, новий створюється), відкликання.
/// Повторне використання ВЖЕ відкликаного токена — ознака викрадення: відкликаються всі активні токени користувача.
/// </summary>
public sealed class RefreshTokenService(AuthDbContext db, IJwtTokenService jwt, IOptions<JwtSettings> options,
    ILogger<RefreshTokenService> logger)
{
    private readonly JwtSettings _settings = options.Value;

    public async Task<(string Token, DateTime ExpiresAt)> IssueAsync(string userId, string? ip, CancellationToken ct)
    {
        var (token, hash) = jwt.GenerateRefreshToken();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenDays),
            CreatedByIp = ip
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (token, entity.ExpiresAt);
    }

    /// <summary>Rotation. Повертає (userId, новий токен) або null, якщо токен недійсний.</summary>
    public async Task<(string UserId, string Token, DateTime ExpiresAt)?> RotateAsync(string token, string? ip, CancellationToken ct)
    {
        var hash = jwt.Hash(token);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;

        if (existing.RevokedAt is not null)
        {
            // reuse detection
            var revoked = await RevokeAllAsync(existing.UserId, "Reuse of revoked refresh token", ct);
            logger.LogWarning("Revoked refresh token reused for user {UserId}: {Count} active tokens revoked", existing.UserId, revoked);
            return null;
        }
        if (!existing.IsActive) return null;                       // прострочений

        var (newToken, newHash) = jwt.GenerateRefreshToken();
        existing.RevokedAt = DateTime.UtcNow;
        existing.ReplacedByTokenHash = newHash;
        existing.ReasonRevoked = "Replaced by rotation";
        var replacement = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenDays),
            CreatedByIp = ip
        };
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);                              // відкликання + новий токен — одна транзакція
        return (existing.UserId, newToken, replacement.ExpiresAt);
    }

    /// <summary>Відкликати один токен; false — токена немає, він чужий або вже неактивний.</summary>
    public async Task<bool> RevokeAsync(string token, string? userId, string reason, CancellationToken ct)
    {
        var hash = jwt.Hash(token);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null || !existing.IsActive || (userId is not null && existing.UserId != userId)) return false;
        existing.RevokedAt = DateTime.UtcNow;
        existing.ReasonRevoked = reason;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RevokeAllAsync(string userId, string reason, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow).ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.ReasonRevoked = reason;
        }
        await db.SaveChangesAsync(ct);
        return active.Count;
    }
}
