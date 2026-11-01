using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Gocgun.Cache.Ui.Redis;

internal sealed class StackExchangeExecutorFactory : IRedisExecutorFactory, IDisposable
{
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(16);

    private readonly Dictionary<string, Held> connections = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly Action<string>? probe;
    private readonly Func<RedisEndpoint, ConnectionMultiplexer> connect;

    public StackExchangeExecutorFactory()
        : this(null)
    {
    }

    internal StackExchangeExecutorFactory(Action<string>? probe)
        : this(probe, Connect)
    {
    }

    internal StackExchangeExecutorFactory(Action<string>? probe, Func<RedisEndpoint, ConnectionMultiplexer> connect)
    {
        this.probe = probe;
        this.connect = connect;
    }

    /// <inheritdoc />
    public IRedisExecutor Create(RedisEndpoint endpoint)
    {
        // One connection per session. The fingerprint covers every field including the password,
        // so a changed credential never reuses an authenticated multiplexer.
        var fingerprint = Fingerprint(endpoint);
        var cached = Find(endpoint.SessionId, fingerprint);
        if (cached is not null)
        {
            return Executor(cached, endpoint, probe);
        }

        // Connecting can take seconds (DNS, TLS, an unreachable host), so it runs outside the lock:
        // one slow server must not stall every other session.
        var fresh = connect(endpoint);
        ConnectionMultiplexer? replaced = null;
        ConnectionMultiplexer? unused = null;
        ConnectionMultiplexer mux;
        lock (gate)
        {
            if (connections.TryGetValue(endpoint.SessionId, out var held) && string.Equals(held.Hash, fingerprint, StringComparison.Ordinal))
            {
                // Another request of this session connected first; keep its connection.
                mux = held.Mux;
                unused = fresh;
            }
            else
            {
                replaced = held?.Mux;
                mux = fresh;
                connections[endpoint.SessionId] = new Held(fingerprint, fresh);
            }
        }

        // If the session is released while this connect runs, the new entry outlives that release.
        // It is freed by the next release or sweep of the same sid, or on shutdown.
        replaced?.Dispose();
        unused?.Dispose();
        return Executor(mux, endpoint, probe);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string sessionId)
    {
        Held? held;
        lock (gate)
        {
            connections.Remove(sessionId, out held);
        }

        if (held is not null)
        {
            await held.Mux.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var held in connections.Values)
            {
                held.Mux.Dispose();
            }

            connections.Clear();
        }
    }

    internal static string Fingerprint(RedisEndpoint endpoint)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{endpoint.Host}\0{endpoint.Port}\0{endpoint.UserName}\0{endpoint.Password}\0{endpoint.Tls}\0{endpoint.Database}");
        return Convert.ToHexString(HMACSHA256.HashData(Salt, Encoding.UTF8.GetBytes(text)));
    }

    private static StackExchangeRedisExecutor Executor(ConnectionMultiplexer mux, RedisEndpoint endpoint, Action<string>? probe)
    {
        return new StackExchangeRedisExecutor(mux.GetDatabase(endpoint.Database), probe);
    }

    private static ConnectionMultiplexer Connect(RedisEndpoint endpoint)
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = true,
            Ssl = endpoint.Tls,
            DefaultDatabase = endpoint.Database,
            CommandMap = StackExchangeRedisExecutor.DenyDangerousCommands(),
        };
        if (!string.IsNullOrEmpty(endpoint.UserName))
        {
            options.User = endpoint.UserName;
        }

        if (!string.IsNullOrEmpty(endpoint.Password))
        {
            options.Password = endpoint.Password;
        }

        options.EndPoints.Add(endpoint.Host, endpoint.Port);
        return ConnectionMultiplexer.Connect(options);
    }

    private ConnectionMultiplexer? Find(string sessionId, string fingerprint)
    {
        lock (gate)
        {
            return connections.TryGetValue(sessionId, out var held) && string.Equals(held.Hash, fingerprint, StringComparison.Ordinal)
                ? held.Mux
                : null;
        }
    }

    private sealed record Held(string Hash, ConnectionMultiplexer Mux);
}
