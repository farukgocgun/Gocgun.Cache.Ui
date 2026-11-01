using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>First-run password setup. Needs the token printed to the console at startup. Disabled after a hash exists.</summary>
[AllowAnonymous]
public sealed class SetupModel : PageModel
{
    private readonly LocalAdminStore store;
    private readonly SetupToken token;
    private readonly AuditLog audit;

    internal SetupModel(LocalAdminStore store, SetupToken token, AuditLog audit)
    {
        this.store = store;
        this.token = token;
        this.audit = audit;
    }

    /// <summary>Gets or sets the setup token from the console.</summary>
    [BindProperty]
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets or sets the new password.</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the confirmation password.</summary>
    [BindProperty]
    public string Confirm { get; set; } = string.Empty;

    /// <summary>Gets the validation error.</summary>
    public string? Error { get; private set; }

    /// <summary>Shows the form, or hides it once a password exists.</summary>
    /// <returns>The page or 404.</returns>
    public IActionResult OnGet()
    {
        return store.IsConfigured ? NotFound() : Page();
    }

    /// <summary>Stores the hash and signs the operator in.</summary>
    /// <returns>The next page or the form.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        if (store.IsConfigured)
        {
            return NotFound();
        }

        if (!token.Matches(Token))
        {
            SessionLifecycle.Record(audit, HttpContext, "setup-failed");
            Error = "Setup token is invalid.";
            return Page();
        }

        if (!string.Equals(Password, Confirm, StringComparison.Ordinal))
        {
            Error = "Passwords do not match.";
            return Page();
        }

        try
        {
            store.SetPassword(Password);
        }
        catch (ArgumentException)
        {
            Error = "Password must be at least 12 characters.";
            return Page();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        await SessionLifecycle.SignInAsync(HttpContext);
        SessionLifecycle.Record(audit, HttpContext, "setup");
        return RedirectToPage("/Index");
    }
}
