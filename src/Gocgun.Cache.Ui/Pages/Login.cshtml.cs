using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>Local password login. Repeated failures lock the login for a while; the console unlock code lifts the lock.</summary>
[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly LocalAdminStore store;
    private readonly LoginThrottle throttle;
    private readonly AuditLog audit;

    internal LoginModel(LocalAdminStore store, LoginThrottle throttle, AuditLog audit)
    {
        this.store = store;
        this.throttle = throttle;
        this.audit = audit;
    }

    /// <summary>Gets or sets the password.</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-time unlock code printed to the console when a lockout starts.</summary>
    [BindProperty]
    public string? UnlockCode { get; set; }

    /// <summary>Gets a value indicating whether the login is locked, so the form asks for the unlock code.</summary>
    public bool Locked { get; private set; }

    /// <summary>Gets the login error.</summary>
    public string? Error { get; private set; }

    /// <summary>Shows the login form.</summary>
    public void OnGet()
    {
        Locked = throttle.IsLocked();
    }

    /// <summary>Checks the hash and issues the cookie.</summary>
    /// <returns>The next page or the form.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        if (throttle.IsLocked())
        {
            if (!throttle.TryUnlock(UnlockCode))
            {
                Locked = true;
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Error = "Too many failed attempts. Enter the unlock code printed in the tool's console, or try again later.";
                return Page();
            }

            SessionLifecycle.Record(audit, HttpContext, "login-unlocked");
        }

        if (!store.Verify(Password))
        {
            var locked = throttle.RecordFailure();
            SessionLifecycle.Record(audit, HttpContext, locked ? "login-failed-lockout" : "login-failed");
            Error = "Login failed.";
            return Page();
        }

        throttle.Reset();
        await SessionLifecycle.SignInAsync(HttpContext);
        SessionLifecycle.Record(audit, HttpContext, "login");
        return RedirectToPage("/Index");
    }
}
