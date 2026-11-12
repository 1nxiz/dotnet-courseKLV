using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Platform.IdentityServer.Data;

public class ApplicationUser : IdentityUser;

/// <summary>Користувачі IdentityServer (ASP.NET Identity) — у тій самій окремій базі IdentityServerDb.</summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options);
