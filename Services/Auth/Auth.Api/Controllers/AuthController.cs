using System.Security.Claims;
using Auth.Api.Data;
using Auth.Api.Dtos;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth.Api.Controllers;

/// <summary>
/// Автентифікація (Таблиця 2 методички): реєстрація з підтвердженням email, login (JWT 15 хв + refresh token 7 днів
/// у HttpOnly cookie), refresh з rotation, revoke, logout, відновлення й зміна пароля.
/// </summary>
[ApiController]
[Route("auth")]
[Produces("application/json")]
public class AuthController(
    UserManager<ApplicationUser> users,
    IJwtTokenService jwt,
    RefreshTokenService refreshTokens,
    IEmailSender email,
    IOptions<JwtSettings> jwtOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    public const string RefreshCookie = "refresh_token";

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? CurrentUserId => User.FindFirstValue("sub");

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email, FullName = request.FullName };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityProblem(result);

        await users.AddToRoleAsync(user, AppRoles.User);                    // роль User — автоматично
        await SendVerificationAsync(user, ct);
        logger.LogInformation("User {UserId} ({UserName}) registered, verification email sent", user.Id, user.UserName);
        return StatusCode(StatusCodes.Status201Created, new { user.Id, user.UserName, user.Email, emailConfirmed = false });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = request.UserNameOrEmail.Contains('@')
            ? await users.FindByEmailAsync(request.UserNameOrEmail)
            : await users.FindByNameAsync(request.UserNameOrEmail);
        if (user is null) return Problem(statusCode: 401, title: "Invalid credentials");

        if (await users.IsLockedOutAsync(user))
            return Problem(statusCode: 403, title: "Account is locked", detail: $"Locked until {user.LockoutEnd:u}");
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);                            // lockout після 5 невдалих спроб
            logger.LogWarning("Failed login for user {UserId}", user.Id);
            return Problem(statusCode: 401, title: "Invalid credentials");
        }
        if (!user.EmailConfirmed)
            return Problem(statusCode: 403, title: "Email is not confirmed", detail: "Confirm the email before logging in");

        await users.ResetAccessFailedCountAsync(user);
        return Ok(await IssueTokensAsync(user, ct));
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(RefreshRequest? request, CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;
        if (string.IsNullOrEmpty(token)) return Problem(statusCode: 401, title: "Refresh token is missing");

        var rotated = await refreshTokens.RotateAsync(token, Ip, ct);
        if (rotated is null)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
            return Problem(statusCode: 401, title: "Refresh token is invalid, expired or revoked");
        }

        var user = await users.FindByIdAsync(rotated.Value.UserId);
        if (user is null) return Problem(statusCode: 401, title: "User not found");
        var roles = await users.GetRolesAsync(user);
        var (access, expires) = jwt.GenerateAccessToken(user, roles);
        SetRefreshCookie(rotated.Value.Token, rotated.Value.ExpiresAt);
        return Ok(new TokenResponse(access, expires, "Bearer", user.Id, user.UserName!, roles.ToList(), rotated.Value.Token));
    }

    [HttpPost("revoke-token")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeToken(RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken ?? Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token)) return Problem(statusCode: 400, title: "Refresh token is missing");
        var isAdmin = User.FindAll("role").Any(r => r.Value == AppRoles.Admin);
        if (!await refreshTokens.RevokeAsync(token, isAdmin ? null : CurrentUserId, "Revoked by user", ct))
            return Problem(statusCode: 400, title: "Token not found or already inactive");
        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest? request, CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;
        if (!string.IsNullOrEmpty(token))
            await refreshTokens.RevokeAsync(token, CurrentUserId, "Logout", ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
        logger.LogInformation("User {UserId} logged out", CurrentUserId);
        return NoContent();                                                 // access token доживе свої <= 15 хв
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null) return Problem(statusCode: 400, title: "Invalid verification token");
        if (user.EmailConfirmed) return Problem(statusCode: 400, title: "Email is already confirmed");   // токен одноразовий
        var result = await users.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded ? NoContent() : Problem(statusCode: 400, title: "Invalid or expired verification token");
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false }) await SendVerificationAsync(user, ct);
        return Accepted();                                                  // однакова відповідь — не розкриваємо, чи є email
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);  // одноразовий: зміна SecurityStamp інвалідує його
            await email.SendAsync(user.Email!, "Password reset",
                $"Reset link: /auth/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}\ntoken: {token}", ct);
        }
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return Problem(statusCode: 400, title: "Invalid or expired reset token");
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded) return IdentityProblem(result, "Invalid or expired reset token");
        var revoked = await refreshTokens.RevokeAllAsync(user.Id, "Password reset", ct);   // усі сесії — геть
        logger.LogInformation("Password reset for user {UserId}, {Revoked} refresh tokens revoked", user.Id, revoked);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(CurrentUserId!);
        if (user is null) return Unauthorized();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    /// <summary>Development: «поштова скринька» mock email service — для тестів підтвердження email і скидання пароля.</summary>
    [HttpGet("dev/mailbox/{email}")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Mailbox(string email, [FromServices] IWebHostEnvironment env, [FromServices] IEmailSender sender)
        => env.IsDevelopment() && sender is DevEmailSender dev ? Ok(dev.Read(email)) : NotFound();

    // ---------------------------------------------------------------------------------------------------------

    private async Task<TokenResponse> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var (access, expires) = jwt.GenerateAccessToken(user, roles);
        var (refresh, refreshExpires) = await refreshTokens.IssueAsync(user.Id, Ip, ct);
        SetRefreshCookie(refresh, refreshExpires);
        logger.LogInformation("User {UserId} logged in with roles {Roles}", user.Id, roles);
        return new TokenResponse(access, expires, "Bearer", user.Id, user.UserName!, roles.ToList(), refresh);
    }

    private async Task SendVerificationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(user.Email!, "Confirm your email",
            $"Confirm link: /auth/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}\nuserId: {user.Id}\ntoken: {token}", ct);
    }

    /// <summary>HttpOnly — недоступний JavaScript (XSS не викраде); SameSite=Strict — не йде з чужих сайтів (CSRF); Path=/auth.</summary>
    private CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expires
    };

    private void SetRefreshCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token, CookieOptions(expires));

    private IActionResult IdentityProblem(IdentityResult result, string title = "Request is invalid")
    {
        foreach (var error in result.Errors) ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(title: title, modelStateDictionary: ModelState);
    }
}
