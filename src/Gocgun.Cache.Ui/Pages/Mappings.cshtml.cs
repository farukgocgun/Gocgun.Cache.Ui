using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Hosting;
using Gocgun.Diagnostics.Shared.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>Saves key-pattern to contract mappings. The file has no values.</summary>
public sealed class MappingsModel : PageModel
{
    private readonly SessionStore<CacheSession> sessions;
    private readonly PatternMapStore store;
    private readonly AuditLog audit;
    private readonly DiagnosticsOptions options;

    internal MappingsModel(SessionStore<CacheSession> sessions, PatternMapStore store, AuditLog audit, DiagnosticsOptions options)
    {
        this.options = options;
        this.sessions = sessions;
        this.store = store;
        this.audit = audit;
    }

    /// <summary>Gets or sets the contract folder.</summary>
    [BindProperty]
    public string Folder { get; set; } = string.Empty;

    /// <summary>Gets or sets the key pattern.</summary>
    [BindProperty]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Gets or sets the contract type name.</summary>
    [BindProperty]
    public string ContractType { get; set; } = string.Empty;

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the exported JSON.</summary>
    public string ExportText { get; private set; } = "[]";

    /// <summary>Shows the saved maps.</summary>
    /// <returns>A task that completes when the maps are loaded.</returns>
    public async Task OnGetAsync()
    {
        ExportText = store.Export();
        var session = await CachePages.SessionAsync(HttpContext, sessions);
        session.Maps.Clear();
        session.Maps.AddRange(store.Load());
    }

    /// <summary>Saves one mapping and loads assemblies from the folder.</summary>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        var session = await CachePages.SessionAsync(HttpContext, sessions);
        if (!string.IsNullOrWhiteSpace(Folder))
        {
            try
            {
                session.Catalog = ContractLoader.Load(Folder, options, audit, session.Profile);
            }
            catch (DirectoryNotFoundException)
            {
                Error = "Contract folder does not exist.";
                ExportText = store.Export();
                return Page();
            }
            catch (InvalidOperationException ex)
            {
                Error = ex.Message;
                ExportText = store.Export();
                return Page();
            }
        }

        if (string.IsNullOrWhiteSpace(Pattern) || string.IsNullOrWhiteSpace(ContractType))
        {
            Error = "Pattern and contract type are required.";
            ExportText = store.Export();
            return Page();
        }

        var maps = store.Load().ToList();
        maps.Add(new PatternMap(Pattern.Trim(), ContractType.Trim()));
        store.Save(maps);
        session.Maps.Clear();
        session.Maps.AddRange(maps);
        ExportText = store.Export();
        CachePages.Audit(audit, session, Pattern.Trim(), ContractType.Trim(), "map");
        return Page();
    }
}
