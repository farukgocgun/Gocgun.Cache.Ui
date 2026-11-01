using StackExchange.Redis;

namespace Gocgun.Cache.Ui.Redis;

internal sealed class StackExchangeRedisExecutor : IRedisExecutor
{
    private readonly IDatabase database;
    private readonly Action<string>? probe;

    internal StackExchangeRedisExecutor(IDatabase database, Action<string>? probe)
    {
        this.database = database;
        this.probe = probe;
    }

    public async Task<RedisBulk> ExecuteAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        RedisCommandGate.Demand(command, args);
        cancellationToken.ThrowIfCancellationRequested();
        probe?.Invoke(command);
        var result = await database.ExecuteAsync(command, args.Cast<object>().ToArray());
        return Map(result);
    }

    internal static CommandMap DenyDangerousCommands()
    {
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "KEYS", "SET", "DEL", "UNLINK", "FLUSHDB", "FLUSHALL", "MSET", "GETSET", "APPEND",
            "HSET", "HDEL", "LPUSH", "RPUSH", "SADD", "ZADD", "PUBLISH", "EVAL", "SCRIPT",
            "CONFIG", "DEBUG", "SHUTDOWN", "SAVE", "BGSAVE", "RENAME", "EXPIRE", "PERSIST", "RESTORE",
        };
        return CommandMap.Create(denied, available: false);
    }

    private static RedisBulk Map(RedisResult result)
    {
        if (result.IsNull)
        {
            return new RedisBulk { IsNull = true };
        }

        switch (result.Resp2Type)
        {
            case ResultType.Integer:
                return new RedisBulk { Integer = (long)result };
            case ResultType.SimpleString:
            case ResultType.BulkString:
                return new RedisBulk { Text = (string?)result, Bytes = (byte[]?)result };
            case ResultType.Array:
                var children = (RedisResult[]?)result ?? [];
                return new RedisBulk { Items = children.Select(Map).ToArray() };
            default:
                return new RedisBulk { Text = result.ToString() };
        }
    }
}
