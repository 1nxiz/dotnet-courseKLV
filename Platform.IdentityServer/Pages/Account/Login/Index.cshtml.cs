using Duende.IdentityServer;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer.Pages.Account.Login;

/// <summary>
/// Сторінка логіну Authorization Code Flow: перевіряє returnUrl через IIdentityServerInteractionService (лише URL-и
/// /connect/authorize/callback зареєстрованих клієнтів), автентифікує через SignInManager і повертає на авторизацію.
/// </summary>
[AllowAnonymous]
public class IndexModel(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    IIdentityServerInteractionService interaction,
    IEventService events,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; private set; }

    public IActionResult OnGet(string? returnUrl)
    {
        Input = new InputModel { ReturnUrl = returnUrl };
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var context = await interaction.GetAuthorizationContextAsync(Input.ReturnUrl);

        if (Input.Button != "login")
        {
            if (context is null) return Redirect("~/");
            await interaction.DenyAuthorizationAsync(context, AuthorizationError.AccessDenied);   // клієнт отримає access_denied
            return Redirect(Input.ReturnUrl!);
        }

        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            Error = "Введіть ім'я користувача та пароль";
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(Input.Username, Input.Password, Input.RememberLogin, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var user = await users.FindByNameAsync(Input.Username);
            await events.RaiseAsync(new UserLoginSuccessEvent(user!.UserName, user.Id, user.UserName, clientId: context?.Client.ClientId));
            logger.LogInformation("User {UserName} signed in for client {ClientId}", user.UserName, context?.Client.ClientId);

            if (context is not null) return Redirect(Input.ReturnUrl!);                 // назад у /connect/authorize/callback
            if (Url.IsLocalUrl(Input.ReturnUrl)) return Redirect(Input.ReturnUrl!);
            if (string.IsNullOrEmpty(Input.ReturnUrl)) return Redirect("~/");
            throw new InvalidOperationException("Invalid return URL");               // захист від open redirect
        }

        await events.RaiseAsync(new UserLoginFailureEvent(Input.Username, result.IsLockedOut ? "locked out" : "invalid credentials",
            clientId: context?.Client.ClientId));
        Error = result.IsLockedOut ? "Обліковий запис тимчасово заблоковано" : "Невірне ім'я користувача або пароль";
        return Page();
    }

    public class InputModel
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool RememberLogin { get; set; }
        public string? ReturnUrl { get; set; }
        public string? Button { get; set; }
    }
}
