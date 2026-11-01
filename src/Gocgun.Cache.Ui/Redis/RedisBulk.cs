namespace Gocgun.Cache.Ui.Redis;

internal sealed class RedisBulk
{
    internal string? Text { get; init; }

    internal byte[]? Bytes { get; init; }

    internal long Integer { get; init; }

    internal bool IsNull { get; init; }

    internal IReadOnlyList<RedisBulk>? Items { get; init; }
}
