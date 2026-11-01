using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Hosting;
using Gocgun.Diagnostics.Shared.Profiles;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gocgun.Cache.Ui;

/// <summary>Entry point for the cache diagnostic tool.</summary>
public partial class Program
{
    /// <summary>Starts Kestrel on loopback. <c>--urls</c> must also be a loopback host.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The host task.</returns>
    public static Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        DiagnosticsHost.Add(builder, "gocgun-cache-ui");
        var data = builder.Configuration["Diagnostics:DataDirectory"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gocgun-cache-ui");
        builder.Services.AddSingleton(new PatternMapStore(Path.Combine(data, "maps.json")));
        builder.Services.AddSingleton<SessionStore<CacheSession>>();
        builder.Services.AddSingleton<ISessionSweeper>(services => services.GetRequiredService<SessionStore<CacheSession>>());
        builder.Services.AddSingleton<IRedisExecutorFactory, StackExchangeExecutorFactory>();
        builder.Services.AddSingleton<ICacheReader, RedisCacheReader>();
        builder.Services.RemoveAll<IPageModelActivatorProvider>();
        builder.Services.AddSingleton<IPageModelActivatorProvider, PageActivator>();
        var app = builder.Build();
        DiagnosticsHost.Use(app);
        app.MapRazorPages();
        return app.RunAsync();
    }
}
