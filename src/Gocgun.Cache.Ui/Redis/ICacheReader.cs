namespace Gocgun.Cache.Ui.Redis;

internal interface ICacheReader
{
    Task<ScanPage> ScanAsync(RedisEndpoint endpoint, string pattern, int pageSize, string cursor, CancellationToken cancellationToken);

    Task<KeyRead> ReadAsync(RedisEndpoint endpoint, string key, CancellationToken cancellationToken);
}
