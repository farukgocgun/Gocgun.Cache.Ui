namespace Gocgun.Cache.Ui.Redis;

internal sealed class GatedExecutor : IRedisExecutor
{
    private readonly IRedisExecutor inner;

    internal GatedExecutor(IRedisExecutor inner)
    {
        this.inner = inner;
    }

    public Task<RedisBulk> ExecuteAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        RedisCommandGate.Demand(command, args);
        cancellationToken.ThrowIfCancellationRequested();
        return inner.ExecuteAsync(command, args, cancellationToken);
    }
}
