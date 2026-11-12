using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer;

/// <summary>
/// Під час старту: міграції трьох контекстів (ConfigurationDbContext, PersistedGrantDbContext, ApplicationDbContext) і
/// засів конфігурації (clients, resources, scopes) та тестових користувачів для кожної ролі.
/// </summary>
public static class SeedData
{
    public static readonly (string UserName, string Role)[] TestUsers =
    [
        ("alice", Permissions.Roles.Admin), ("mike", Permissions.Roles.Manager),
        ("uma", Permissions.Roles.User), ("gary", Permissions.Roles.Guest)
    ];

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await sp.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();
                await sp.GetRequiredService<ConfigurationDbContext>().Database.MigrateAsync();
                await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
                break;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "IdentityServerDb is not ready (attempt {Attempt})", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        var config = sp.GetRequiredService<ConfigurationDbContext>();
        // конфігурація з коду — джерело істини: перезаписуємо при кожному старті (ідемпотентно)
        config.Clients.RemoveRange(await config.Clients.ToListAsync());
        config.IdentityResources.RemoveRange(await config.IdentityResources.ToListAsync());
        config.ApiScopes.RemoveRange(await config.ApiScopes.ToListAsync());
        config.ApiResources.RemoveRange(await config.ApiResources.ToListAsync());
        await config.SaveChangesAsync();
        config.Clients.AddRange(Config.Clients(configuration).Select(c => c.ToEntity()));
        config.IdentityResources.AddRange(Config.IdentityResources.Select(r => r.ToEntity()));
        config.ApiScopes.AddRange(Config.ApiScopes.Select(s => s.ToEntity()));
        config.ApiResources.AddRange(Config.ApiResources.Select(r => r.ToEntity()));
        await config.SaveChangesAsync();
        logger.LogInformation("IdentityServer configuration seeded: {Clients} clients, {Scopes} API scopes",
            config.Clients.Count(), config.ApiScopes.Count());

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Permissions.RoleMatrix.Keys)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var password = configuration["Seed:Password"] ?? "Pass_123456";
        foreach (var (userName, role) in TestUsers)
        {
            if (await users.FindByNameAsync(userName) is not null) continue;
            var user = new ApplicationUser { UserName = userName, Email = $"{userName}@library.local", EmailConfirmed = true };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogWarning("Seeding {User} failed: {Errors}", userName, string.Join("; ", result.Errors.Select(e => e.Description)));
                continue;
            }
            await users.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded user {User} with role {Role}", userName, role);
        }
    }
}
