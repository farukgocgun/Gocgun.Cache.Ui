using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Cache.Ui.Pages;

/// <summary>Shows one key. The audit line records the key name, not the bytes.</summary>
public sealed class ValueModel : PageModel
{
    private readonly SessionStore<CacheSession> sessions;
    private readonly ICacheReader reader;
    private readonly AuditLog audit;

    internal ValueModel(SessionStore<CacheSession> sessions, ICacheReader reader, AuditLog audit)
    {
        this.sessions = sessions;
        this.reader = reader;
        this.audit = audit;
    }

    /// <summary>Gets the key name.</summary>
    public string KeyName { get; private set; } = string.Empty;

    /// <summary>Gets the Redis type.</summary>
    public string TypeName { get; private set; } = string.Empty;

    /// <summary>Gets the tenant segment.</summary>
    public string TenantName { get; private set; } = string.Empty;

    /// <summary>Gets the decoded text.</summary>
    public string? Display { get; private set; }

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Reads and decodes the key.</summary>
    /// <param name="key">Redis key.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnGetAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Error = "Key is required.";
            return Page();
        }

        var session = await CachePages.SessionAsync(HttpContext, sessions);
        if (session.Endpoint is null)
        {
            Error = "Connect before reading a key.";
            return Page();
        }

        try
        {
            var read = await reader.ReadAsync(session.Endpoint, key, cancellationToken);
            KeyName = read.Key;
            TypeName = read.Type;
            TenantName = read.Tenant ?? string.Empty;
            Display = Describe(session, read);
            var contract = PatternMatcher.FindContract(session.Maps, key);
            CachePages.Audit(audit, session, key, contract, "view");
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

    private static string Describe(CacheSession session, KeyRead read)
    {
        if (read.StringValue is null)
        {
            return read.CollectionPreview.Length == 0
                ? FallbackDecoder.Describe([]).ToText()
                : read.CollectionPreview;
        }

        var contract = PatternMatcher.FindContract(session.Maps, read.Key);
        if (contract is not null
            && session.Catalog.TryParse(contract, read.StringValue, out var message, out _)
            && message is not null)
        {
            return CipherDisplay.Format(message);
        }

        return FallbackDecoder.Describe(read.StringValue).ToText();
    }
}
