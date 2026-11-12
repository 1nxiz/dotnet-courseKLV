using System.ComponentModel.DataAnnotations;

namespace Auth.Api.Dtos;

public record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3)] string UserName,
    [Required, EmailAddress] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    string? FullName);

public record LoginRequest([Required] string UserNameOrEmail, [Required] string Password);

/// <summary>Access token — у тілі відповіді; refresh token — у HttpOnly cookie (і в полі лише для не-браузерних клієнтів).</summary>
public record TokenResponse(string AccessToken, DateTime ExpiresAt, string TokenType, string UserId, string UserName,
    IReadOnlyList<string> Roles, string? RefreshToken = null);

/// <summary>Refresh / revoke: токен береться з cookie, або з тіла (Postman, мобільні клієнти).</summary>
public record RefreshRequest(string? RefreshToken);

public record VerifyEmailRequest([Required] string UserId, [Required] string Token);

public record EmailRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest([Required, EmailAddress] string Email, [Required] string Token,
    [Required, StringLength(100, MinimumLength = 8)] string NewPassword);

public record ChangePasswordRequest([Required] string CurrentPassword, [Required, StringLength(100, MinimumLength = 8)] string NewPassword);

public record UserDto(string Id, string UserName, string? Email, bool EmailConfirmed, string? FullName, DateTime CreatedAt,
    bool LockedOut, IReadOnlyList<string> Roles);

public record UpdateUserRequest([StringLength(200)] string? FullName, [EmailAddress] string? Email);

public record AssignRoleRequest([Required] string Role);

public record RefreshTokenDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt, string? ReasonRevoked,
    bool IsActive, string? CreatedByIp);
