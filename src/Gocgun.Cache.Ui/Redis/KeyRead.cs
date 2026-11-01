namespace Gocgun.Cache.Ui.Redis;

internal sealed record KeyRead(string Key, string Type, byte[]? StringValue, string CollectionPreview, string? Tenant);
