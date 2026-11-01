using System.Globalization;

namespace Gocgun.Cache.Ui.Redis;

internal sealed class RedisEndpoint
{
    internal RedisEndpoint(string host, int port, string userName, string password, bool tls, int database, string sessionId)
    {
        SessionId = sessionId;
        Host = host;
        Port = port;
        UserName = userName;
        Password = password;
        Tls = tls;
        Database = database;
    }

    internal string SessionId { get; }

    internal string Host { get; }

    internal int Port { get; }

    internal string UserName { get; }

    internal string Password { get; }

    internal bool Tls { get; }

    internal int Database { get; }

    public override string ToString()
    {
        return Host + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/" + Database.ToString(CultureInfo.InvariantCulture);
    }
}
