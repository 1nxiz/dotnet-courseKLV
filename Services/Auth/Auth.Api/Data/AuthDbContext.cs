using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Data;

/// <summary>Користувач платформи: стандартні поля Identity + профіль.</summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Refresh token у БД: зберігається лише SHA-256 хеш (викрадена БД не дає робочих токенів), дати створення,
/// закінчення (7 днів) і відкликання; ReplacedByTokenHash — ланцюжок rotation (повторне використання відкликаного
/// токена = ознака крадіжки -> відкликаються всі токени користувача).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? ReasonRevoked { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

/// <summary>Окрема база IdentityDb: таблиці AspNetUsers, AspNetRoles, AspNetUserRoles, ... + RefreshTokens.</summary>
public class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(u => u.Property(x => x.FullName).HasMaxLength(200));
        builder.Entity<RefreshToken>(t =>
        {
            t.ToTable("RefreshTokens");
            t.HasKey(x => x.Id);
            t.Property(x => x.UserId).IsRequired().HasMaxLength(450);
            t.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
            t.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            t.Property(x => x.ReasonRevoked).HasMaxLength(200);
            t.Property(x => x.CreatedByIp).HasMaxLength(64);
            t.HasIndex(x => x.TokenHash).IsUnique();                  // пошук токена під час refresh
            t.HasIndex(x => new { x.UserId, x.CreatedAt });           // історія токенів користувача
            t.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            t.Ignore(x => x.IsActive);
        });
    }
}

/// <summary>Ролі Auth Service (у JWT — claim "role"; матриця permissions — Permissions.RoleMatrix).</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Manager = "Manager";
    public const string Guest = "Guest";
    public static readonly string[] All = [Admin, User, Manager, Guest];
}

/// <summary>Ролі й адміністратор створюються під час старту (ідемпотентно).</summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = configuration["Seed:AdminEmail"] ?? "admin@library.local";
        if (await users.FindByEmailAsync(email) is null)
        {
            var admin = new ApplicationUser { UserName = "admin", Email = email, EmailConfirmed = true, FullName = "Library Admin" };
            var result = await users.CreateAsync(admin, configuration["Seed:AdminPassword"] ?? "Admin_Pass123!");
            if (result.Succeeded)
            {
                await users.AddToRolesAsync(admin, [AppRoles.Admin, AppRoles.User]);
                logger.LogInformation("Seeded admin user {Email}", email);
            }
            else
                logger.LogWarning("Admin seed failed: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
