using System.Globalization;
using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>SCAN browser. The page has no command field.</summary>
public sealed class KeysModel : PageModel
{
    private readonly SessionStore<CacheSession> sessions;
    private readonly ICacheReader reader;
    private readonly AuditLog audit;

    internal KeysModel(SessionStore<CacheSession> sessions, ICacheReader reader, AuditLog audit)
    {
        this.sessions = sessions;
        this.reader = reader;
        this.audit = audit;
    }

    /// <summary>Gets or sets the match pattern.</summary>
    [BindProperty]
    public string Pattern { get; set; } = "*";

    /// <summary>Gets or sets the page size.</summary>
    [BindProperty]
    public int PageSize { get; set; } = 20;

    /// <summary>Gets or sets the SCAN cursor.</summary>
    [BindProperty]
    public string Cursor { get; set; } = "0";

    /// <summary>Gets or sets the tenant filter.</summary>
    [BindProperty]
    public string Tenant { get; set; } = string.Empty;

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the next cursor.</summary>
    public string NextCursor { get; private set; } = "0";

    /// <summary>Gets the rows.</summary>
    internal IReadOnlyList<KeyRow> Rows { get; private set; } = [];

    /// <summary>Shows an empty browser.</summary>
    public void OnGet()
    {
        // The form renders from its properties. There is no load step.
    }

    /// <summary>Runs SCAN and the per-key read commands.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            Error = "Pattern is required.";
            return Page();
        }

        if (PageSize is < 1 or > 200)
        {
            Error = "Page size must be between 1 and 200.";
            return Page();
        }

        if (!long.TryParse(Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            Error = "Cursor must be a non-negative integer.";
            return Page();
        }

        var session = await CachePages.SessionAsync(HttpContext, sessions);
        if (session.Endpoint is null)
        {
            Error = "Connect before scanning.";
            return Page();
        }

        try
        {
            var page = await reader.ScanAsync(session.Endpoint, Pattern, PageSize, Cursor, cancellationToken);
            var rows = page.Rows;
            if (!string.IsNullOrWhiteSpace(Tenant))
            {
                rows = rows.Where(row => string.Equals(row.Tenant, Tenant.Trim(), StringComparison.Ordinal)).ToArray();
            }

            Rows = rows;
            NextCursor = page.NextCursor;
            CachePages.Audit(audit, session, Pattern, null, "scan");
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        catch (Exception)
        {
            Error = "Redis read failed.";
        }

        return Page();
    }
}
