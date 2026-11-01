using System.Globalization;

namespace Gocgun.Cache.Ui.Redis;

internal static class RedisCommandGate
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCAN", "TYPE", "TTL", "MEMORY", "GET", "STRLEN", "HSCAN", "LRANGE", "SSCAN", "ZSCAN",
    };

    internal static void Demand(string command, IReadOnlyList<string> args)
    {
        if (string.IsNullOrWhiteSpace(command) || !Allowed.Contains(command))
        {
            throw new InvalidOperationException("Redis command is not allowlisted.");
        }

        if (command.Equals("MEMORY", StringComparison.OrdinalIgnoreCase)
            && (args.Count < 2 || !args[0].Equals("USAGE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Only MEMORY USAGE is allowlisted.");
        }

        if (command.Equals("LRANGE", StringComparison.OrdinalIgnoreCase) && !BoundedRange(args))
        {
            throw new InvalidOperationException("LRANGE must cover at most 100 elements.");
        }

        if (IsScan(command))
        {
            DemandCount(args);
        }
    }

    private static bool IsScan(string command)
    {
        return command.Equals("SCAN", StringComparison.OrdinalIgnoreCase)
            || command.Equals("HSCAN", StringComparison.OrdinalIgnoreCase)
            || command.Equals("SSCAN", StringComparison.OrdinalIgnoreCase)
            || command.Equals("ZSCAN", StringComparison.OrdinalIgnoreCase);
    }

    private static void DemandCount(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (!args[i].Equals("COUNT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > 200)
            {
                throw new InvalidOperationException("COUNT must be between 1 and 200.");
            }
        }
    }

    private static bool BoundedRange(IReadOnlyList<string> args)
    {
        if (args.Count != 3)
        {
            return false;
        }

        if (!long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            || !long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stop))
        {
            return false;
        }

        return start >= 0 && stop >= start && stop - start + 1 <= 100;
    }
}
