using Gocgun.Diagnostics.Shared.Hosting;

namespace Gocgun.Cache.Ui.Redis;

internal sealed class RedisCacheReader : ICacheReader
{
    private readonly IRedisExecutorFactory factory;
    private readonly DiagnosticsOptions options;

    internal RedisCacheReader(IRedisExecutorFactory factory, DiagnosticsOptions options)
    {
        this.factory = factory;
        this.options = options;
    }

    /// <inheritdoc />
    public Task<ScanPage> ScanAsync(RedisEndpoint endpoint, string pattern, int pageSize, string cursor, CancellationToken cancellationToken)
    {
        var reader = new RedisReader(factory.Create(endpoint), options.MaxValueBytes);
        return reader.ScanAsync(pattern, pageSize, cursor, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KeyRead> ReadAsync(RedisEndpoint endpoint, string key, CancellationToken cancellationToken)
    {
        var reader = new RedisReader(factory.Create(endpoint), options.MaxValueBytes);
        return reader.ReadAsync(key, cancellationToken);
    }
}
