using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;

namespace Gocgun.Cache.Ui;

internal static class CachePages
{
    internal static ValueTask<CacheSession> SessionAsync(HttpContext http, SessionStore<CacheSession> store)
    {
        var sid = DiagnosticsHost.Sid(http.User) ?? "missing";
        var factory = http.RequestServices.GetRequiredService<IRedisExecutorFactory>();
        return store.GetOrAddAsync(sid, id => new CacheSession(id, factory));
    }

    internal static void Audit(AuditLog log, CacheSession session, string target, string? contract, string action)
    {
        log.Append(new AuditEntry(DiagnosticsHost.OperatorName, session.Profile, target, contract, action));
    }
}
