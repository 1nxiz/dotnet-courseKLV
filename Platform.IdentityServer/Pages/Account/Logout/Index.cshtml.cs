using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer.Pages.Account.Logout;

/// <summary>Вихід (end session): закриває сесію IdentityServer і показує посилання повернення до клієнта.</summary>
[AllowAnonymous]
public class IndexModel(SignInManager<ApplicationUser> signIn, IIdentityServerInteractionService interaction, IEventService events) : PageModel
{
    public string? PostLogoutRedirectUri { get; private set; }

    public async Task<IActionResult> OnGet(string? logoutId)
    {
        var context = await interaction.GetLogoutContextAsync(logoutId);
        if (User.Identity?.IsAuthenticated == true)
        {
            await signIn.SignOutAsync();
            await events.RaiseAsync(new UserLogoutSuccessEvent(User.GetSubjectId(), User.GetDisplayName()));
        }
        PostLogoutRedirectUri = context?.PostLogoutRedirectUri;
        return Page();
    }
}
