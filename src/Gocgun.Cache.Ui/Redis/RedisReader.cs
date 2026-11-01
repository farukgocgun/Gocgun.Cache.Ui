using System.Globalization;
using System.Text;
using Gocgun.Diagnostics.Shared.Tenancy;

namespace Gocgun.Cache.Ui.Redis;

internal sealed class RedisReader
{
    private readonly IRedisExecutor executor;
    private readonly long maxValueBytes;

    internal RedisReader(IRedisExecutor executor, long maxValueBytes)
    {
        this.executor = executor;
        this.maxValueBytes = maxValueBytes;
    }

    internal async Task<ScanPage> ScanAsync(string pattern, int pageSize, string cursor, CancellationToken cancellationToken)
    {
        var count = pageSize.ToString(CultureInfo.InvariantCulture);
        var scanArgs = new[] { cursor, "MATCH", pattern, "COUNT", count };
        RedisCommandGate.Demand("SCAN", scanArgs);
        var scanned = await executor.ExecuteAsync("SCAN", scanArgs, cancellationToken);
        var items = scanned.Items ?? [];
        var next = items.Count > 0 ? items[0].Text ?? "0" : "0";
        var keys = items.Count > 1 ? items[1].Items ?? [] : [];
        var rows = new List<KeyRow>();
        foreach (var key in keys)
        {
            var name = key.Text ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            rows.Add(await DescribeAsync(name, cancellationToken));
        }

        return new ScanPage(next, rows);
    }

    internal async Task<KeyRead> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var type = await TypeAsync(key, cancellationToken);
        if (type.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            var args = new[] { key };
            RedisCommandGate.Demand("STRLEN", args);
            var length = await executor.ExecuteAsync("STRLEN", args, cancellationToken);
            if (length.Integer > maxValueBytes)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Value is {length.Integer} bytes, above the limit of {maxValueBytes} bytes."));
            }

            RedisCommandGate.Demand("GET", args);
            var value = await executor.ExecuteAsync("GET", args, cancellationToken);
            return new KeyRead(key, type, value.IsNull ? null : value.Bytes, string.Empty, TenantMarkers.FromCacheKey(key));
        }

        var preview = await CollectionAsync(key, type, cancellationToken);
        return new KeyRead(key, type, null, preview, TenantMarkers.FromCacheKey(key));
    }

    private static string Join(RedisBulk bulk)
    {
        var builder = new StringBuilder();
        Append(bulk, builder);
        return builder.ToString();
    }

    private static void Append(RedisBulk bulk, StringBuilder builder)
    {
        if (bulk.Items is null)
        {
            if (bulk.Bytes is { Length: > 0 })
            {
                builder.Append(Convert.ToHexString(bulk.Bytes));
                builder.Append(' ');
            }
            else if (bulk.Text is not null)
            {
                builder.Append(bulk.Text);
                builder.Append(' ');
            }

            return;
        }

        foreach (var item in bulk.Items)
        {
            Append(item, builder);
        }
    }

    private async Task<KeyRow> DescribeAsync(string key, CancellationToken cancellationToken)
    {
        var type = await TypeAsync(key, cancellationToken);
        var ttlArgs = new[] { key };
        RedisCommandGate.Demand("TTL", ttlArgs);
        var ttl = await executor.ExecuteAsync("TTL", ttlArgs, cancellationToken);
        long? size = null;
        var memoryArgs = new[] { "USAGE", key };
        RedisCommandGate.Demand("MEMORY", memoryArgs);
        try
        {
            var memory = await executor.ExecuteAsync("MEMORY", memoryArgs, cancellationToken);
            size = memory.Integer;
        }
        catch (StackExchange.Redis.RedisException)
        {
            size = null;
        }

        return new KeyRow(key, type, ttl.Integer, size, TenantMarkers.FromCacheKey(key));
    }

    private async Task<string> TypeAsync(string key, CancellationToken cancellationToken)
    {
        var args = new[] { key };
        RedisCommandGate.Demand("TYPE", args);
        var result = await executor.ExecuteAsync("TYPE", args, cancellationToken);
        return result.Text ?? "none";
    }

    private async Task<string> CollectionAsync(string key, string type, CancellationToken cancellationToken)
    {
        if (type.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var args = new[] { key, "0", "99" };
            RedisCommandGate.Demand("LRANGE", args);
            var result = await executor.ExecuteAsync("LRANGE", args, cancellationToken);
            return Join(result);
        }

        var command = type.ToUpperInvariant() switch
        {
            "HASH" => "HSCAN",
            "SET" => "SSCAN",
            "ZSET" => "ZSCAN",
            _ => string.Empty,
        };
        if (command.Length == 0)
        {
            return string.Empty;
        }

        var scan = new[] { key, "0", "MATCH", "*", "COUNT", "100" };
        RedisCommandGate.Demand(command, scan);
        var page = await executor.ExecuteAsync(command, scan, cancellationToken);
        return Join(page);
    }
}
