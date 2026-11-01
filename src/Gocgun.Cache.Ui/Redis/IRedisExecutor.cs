namespace Gocgun.Cache.Ui.Redis;

internal interface IRedisExecutor
{
    Task<RedisBulk> ExecuteAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken);
}
