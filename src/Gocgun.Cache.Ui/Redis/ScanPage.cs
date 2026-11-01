namespace Gocgun.Cache.Ui.Redis;

internal sealed record ScanPage(string NextCursor, IReadOnlyList<KeyRow> Rows);
