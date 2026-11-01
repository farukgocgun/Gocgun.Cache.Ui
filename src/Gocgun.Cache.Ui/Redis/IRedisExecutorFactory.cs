namespace Gocgun.Cache.Ui.Redis;

internal interface IRedisExecutorFactory
{
    IRedisExecutor Create(RedisEndpoint endpoint);

    /// <summary>Closes the connection a session holds.</summary>
    /// <param name="sessionId">Session id.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    ValueTask ReleaseAsync(string sessionId);
}
