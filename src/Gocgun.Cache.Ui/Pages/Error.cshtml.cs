using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>Generic error page. It shows no exception text.</summary>
[AllowAnonymous]
public sealed class ErrorModel : PageModel
{
    /// <summary>Shows the generic error.</summary>
    public void OnGet()
    {
        // The page shows a fixed message. There is no load step.
    }
}
