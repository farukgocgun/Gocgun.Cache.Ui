using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>Ends the login: drops the in-memory session and credentials, then clears the cookie. POST only, antiforgery-protected.</summary>
public sealed class LogoutModel : PageModel
{
    private readonly SessionStore<CacheSession> sessions;
    private readonly AuditLog audit;

    internal LogoutModel(SessionStore<CacheSession> sessions, AuditLog audit)
    {
        this.sessions = sessions;
        this.audit = audit;
    }

    /// <summary>A GET does not log out.</summary>
    /// <returns>A redirect to the start page.</returns>
    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }

    /// <summary>Signs out.</summary>
    /// <returns>A redirect to the login page.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        await SessionLifecycle.SignOutAsync(HttpContext, sessions, audit);
        return RedirectToPage("/Login");
    }
}
