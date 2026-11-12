using System.Security.Claims;
using Auth.Api.Data;
using Auth.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Controllers;

/// <summary>
/// Керування користувачами й ролями. Admin-операції — [Authorize(Roles = "Admin")]; owner-операції дозволені власнику
/// (sub з токена = {id}) або Admin. Ролі читаються з JWT (claim "role") — без звернення до БД.
/// </summary>
[ApiController]
[Route("users")]
[Authorize]
[Produces("application/json")]
public class UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, AuthDbContext db,
    ILogger<UsersController> logger) : ControllerBase
{
    private bool IsAdmin => User.IsInRole(AppRoles.Admin);
    private bool IsOwnerOrAdmin(string id) => IsAdmin || User.FindFirstValue("sub") == id;

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var list = await users.Users.OrderBy(u => u.UserName).Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<UserDto>();
        foreach (var u in list) result.Add(await ToDtoAsync(u));
        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(string id)
    {
        if (!IsOwnerOrAdmin(id)) return Forbid();
        var user = await users.FindByIdAsync(id);
        return user is null ? Problem(statusCode: 404, title: "User not found") : Ok(await ToDtoAsync(user));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> Update(string id, UpdateUserRequest request)
    {
        if (!IsOwnerOrAdmin(id)) return Forbid();
        var user = await users.FindByIdAsync(id);
        if (user is null) return Problem(statusCode: 404, title: "User not found");
        user.FullName = request.FullName ?? user.FullName;
        if (request.Email is not null && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = request.Email;
            user.EmailConfirmed = false;                                    // новий email треба підтвердити
        }
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return Problem(statusCode: 400, title: string.Join("; ", result.Errors.Select(e => e.Description)));
        return Ok(await ToDtoAsync(user));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Problem(statusCode: 404, title: "User not found");
        await users.DeleteAsync(user);                                      // refresh tokens — каскадно
        logger.LogInformation("User {UserId} deleted by {AdminId}", id, User.FindFirstValue("sub"));
        return NoContent();
    }

    [HttpGet("{id}/refresh-tokens")]
    [ProducesResponseType(typeof(IReadOnlyList<RefreshTokenDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RefreshTokenDto>>> RefreshTokens(string id)
    {
        if (!IsOwnerOrAdmin(id)) return Forbid();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync();
        return Ok(tokens.Select(t => new RefreshTokenDto(t.Id, t.CreatedAt, t.ExpiresAt, t.RevokedAt, t.ReasonRevoked, t.IsActive, t.CreatedByIp)).ToList());
    }

    [HttpGet("{id}/roles")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(string id)
    {
        var user = await users.FindByIdAsync(id);
        return user is null ? Problem(statusCode: 404, title: "User not found") : Ok((await users.GetRolesAsync(user)).ToList());
    }

    [HttpPost("{id}/roles")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRole(string id, AssignRoleRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Problem(statusCode: 404, title: "User not found");
        if (!await roles.RoleExistsAsync(request.Role)) return Problem(statusCode: 400, title: $"Unknown role {request.Role}");
        if (!await users.IsInRoleAsync(user, request.Role)) await users.AddToRoleAsync(user, request.Role);
        logger.LogInformation("Role {Role} assigned to {UserId}", request.Role, id);
        return NoContent();                                                 // нова роль — у наступному access token
    }

    [HttpDelete("{id}/roles/{role}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Problem(statusCode: 404, title: "User not found");
        if (await users.IsInRoleAsync(user, role)) await users.RemoveFromRoleAsync(user, role);
        return NoContent();
    }

    private async Task<UserDto> ToDtoAsync(ApplicationUser u) => new(u.Id, u.UserName!, u.Email, u.EmailConfirmed, u.FullName,
        u.CreatedAt, u.LockoutEnd > DateTimeOffset.UtcNow, (await users.GetRolesAsync(u)).ToList());
}
