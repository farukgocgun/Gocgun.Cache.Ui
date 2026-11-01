using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Decoding;

namespace Gocgun.Cache.Ui;

internal sealed class CacheSession : IAsyncDisposable
{
    private readonly IRedisExecutorFactory factory;

    internal CacheSession(string id, IRedisExecutorFactory factory)
    {
        Id = id;
        this.factory = factory;
    }

    internal string Id { get; }

    internal RedisEndpoint? Endpoint { get; set; }

    internal ContractCatalog Catalog { get; set; } = ContractCatalog.Empty;

    internal List<PatternMap> Maps { get; } = [];

    internal string Profile { get; set; } = "session";

    public ValueTask DisposeAsync()
    {
        return factory.ReleaseAsync(Id);
    }
}
