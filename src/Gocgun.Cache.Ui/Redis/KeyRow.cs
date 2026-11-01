namespace Gocgun.Cache.Ui.Redis;

internal sealed record KeyRow(string Key, string Type, long TtlSeconds, long? SizeBytes, string? Tenant);
