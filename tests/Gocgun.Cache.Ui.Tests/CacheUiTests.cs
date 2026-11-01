using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Gocgun.Cache.Ui.Redis;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Gocgun.Cache.Ui.Tests;

public sealed class CacheUiTests
{
    [Fact]
    public async Task Gate_RejectsKeysWritesAndUnboundedRange_BeforeSend()
    {
        var called = false;
        var inner = new FlagExecutor(() => called = true);
        var gated = new GatedExecutor(inner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("KEYS", ["*"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("SET", ["k", "v"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("DEL", ["k"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("FLUSHALL", [], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("EVAL", ["return 1"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("MEMORY", ["PURGE"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("LRANGE", ["k", "0", "500"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gated.ExecuteAsync("SCAN", ["0", "COUNT", "9999"], CancellationToken.None));
        Assert.False(called);
        _ = StackExchangeRedisExecutor.DenyDangerousCommands();
    }

    [Fact]
    public async Task Reader_Scan_UsesOnlyAllowlistedCommands()
    {
        var executor = new ScriptExecutor();
        var reader = new RedisReader(executor, 1024);
        await reader.ScanAsync("t:tenant-a:*", 10, "0", CancellationToken.None);
        Assert.Contains("SCAN", executor.Commands, StringComparer.Ordinal);
        Assert.Contains("TYPE", executor.Commands, StringComparer.Ordinal);
        Assert.DoesNotContain("KEYS", executor.Commands, StringComparer.Ordinal);
        Assert.DoesNotContain("SET", executor.Commands, StringComparer.Ordinal);
        var read = await reader.ReadAsync("t:tenant-a:item", CancellationToken.None);
        Assert.Equal("tenant-a", read.Tenant);
        Assert.Equal("hello", Encoding.UTF8.GetString(read.StringValue!));
    }

    [Fact]
    public async Task Web_LoginCsrfAuditAndAttackerFields()
    {
        var reader = new RecordingReader();
        await using var factory = new AppFactory { Reader = reader };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var setupToken = factory.Services.GetRequiredService<SetupToken>().Value;
        var setupPage = await client.GetAsync("/Setup");
        Assert.Equal(HttpStatusCode.OK, setupPage.StatusCode);
        Assert.Contains("no-store", setupPage.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var shortToken = await Token(client, "/Setup");
        var shortPost = await client.PostAsync("/Setup", Form(shortToken, new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "short", ["Confirm"] = "short" }));
        Assert.Equal(HttpStatusCode.OK, shortPost.StatusCode);
        Assert.Contains("12 characters", await shortPost.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var bare = new HttpRequestMessage(HttpMethod.Post, "/Setup") { Content = Form(null, new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }) };
        var csrf = await client.SendAsync(bare);
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

        var wrongToken = await client.PostAsync("/Setup", Form(await Token(client, "/Setup"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = "not-the-token", ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Contains("Setup token is invalid.", await wrongToken.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var token = await Token(client, "/Setup");
        var setup = await client.PostAsync("/Setup", Form(token, new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);

        using var stranger = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        foreach (var path in new[] { "/", "/Keys", "/Value", "/Mappings" })
        {
            var blocked = await stranger.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
            Assert.Contains("/Login", blocked.Headers.Location?.OriginalString, StringComparison.Ordinal);
        }

        var forged = new HttpRequestMessage(HttpMethod.Get, "/Keys");
        forged.Headers.TryAddWithoutValidation("Cookie", "gocgun-cache-ui.auth=forged");
        var forgedResponse = await stranger.SendAsync(forged);
        Assert.Equal(HttpStatusCode.Redirect, forgedResponse.StatusCode);

        using var unauthenticated = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await Token(unauthenticated, "/Login");
        var wrong = await unauthenticated.PostAsync("/Login", Form(login, new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "not-the-password" }));
        Assert.Contains("Login failed.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var connectToken = await Token(client, "/");
        var connect = await client.PostAsync("/", Form(connectToken, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Host"] = "127.0.0.1",
            ["Port"] = "1",
            ["UserName"] = "reader",
            ["Password"] = "REDISPASSWORD",
            ["Database"] = "0",
            ["ProfileName"] = "session",
        }));
        Assert.Equal(HttpStatusCode.Redirect, connect.StatusCode);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("REDISPASSWORD", StringComparison.Ordinal));

        var badPortToken = await Token(client, "/");
        var badPort = await client.PostAsync("/", Form(badPortToken, new Dictionary<string, string>(StringComparer.Ordinal) { ["Host"] = "127.0.0.1", ["Port"] = "0", ["Database"] = "0" }));
        Assert.Contains("Port must", await badPort.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var scanToken = await Token(client, "/Keys");
        var scan = await client.PostAsync("/Keys", Form(scanToken, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Pattern"] = "*",
            ["PageSize"] = "10",
            ["Cursor"] = "0",
            ["command"] = "KEYS",
        }));
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(["*"], reader.Scans);

        var emptyPattern = await client.PostAsync("/Keys", Form(await Token(client, "/Keys"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Pattern"] = string.Empty, ["PageSize"] = "10", ["Cursor"] = "0" }));
        Assert.Contains("Pattern is required.", await emptyPattern.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var badSize = await client.PostAsync("/Keys", Form(await Token(client, "/Keys"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Pattern"] = "*", ["PageSize"] = "0", ["Cursor"] = "0" }));
        Assert.Contains("Page size", await badSize.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var value = await client.GetAsync("/Value?key=cache-key");
        var html = await value.Content.ReadAsStringAsync();
        Assert.Contains("PAYLOADSECRET", html, StringComparison.Ordinal);
        var contract = await client.PostAsync("/Mappings", Form(await Token(client, "/Mappings"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Folder"] = "/tmp", ["Pattern"] = "*", ["ContractType"] = "x" }));
        Assert.Contains("Contract loading is disabled", await contract.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var longValue = await client.GetAsync("/Value?key=big-key");
        Assert.Contains("above the limit", await longValue.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var badProfile = await client.PostAsync("/", Form(await Token(client, "/"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Host"] = "127.0.0.1", ["Port"] = "1", ["Database"] = "0", ["ProfileName"] = "../evil" }));
        Assert.Contains("Profile name must", await badProfile.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var audit = await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log"));
        Assert.Contains("\"action\":\"login-failed\"", audit, StringComparison.Ordinal);
        Assert.Contains("contract-load-refused", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", audit, StringComparison.Ordinal);
        Assert.Contains("cache-key", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYLOADSECRET", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("REDISPASSWORD", audit, StringComparison.Ordinal);
        var project = await File.ReadAllTextAsync(FindUp("Gocgun.Cache.Ui.csproj"));
        Assert.Contains("AGPL-3.0-only", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Gocgun.Identity", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_LogoutRevokesCookie_AndLoginLocksAfterFailures()
    {
        await using var factory = new AppFactory { Reader = new RecordingReader() };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var setupToken = factory.Services.GetRequiredService<SetupToken>().Value;
        var setup = await client.PostAsync("/Setup", Form(await Token(client, "/Setup"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        var cookie = setup.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).First(value => value.StartsWith("gocgun-cache-ui.auth=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", string.Join(";", setup.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        var stolen = new HttpRequestMessage(HttpMethod.Get, "/Keys");
        stolen.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        Assert.Equal(HttpStatusCode.OK, (await other.SendAsync(stolen)).StatusCode);

        var getLogout = await client.GetAsync("/Logout");
        Assert.Equal(HttpStatusCode.Redirect, getLogout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Keys")).StatusCode);
        var noToken = await client.PostAsync("/Logout", Form(null, new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        var logout = await client.PostAsync("/Logout", Form(await Token(client, "/Keys"), new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Login", logout.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var replay = new HttpRequestMessage(HttpMethod.Get, "/Keys");
        replay.Headers.TryAddWithoutValidation("Cookie", cookie);
        var replayed = await other.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Redirect, replayed.StatusCode);
        Assert.Contains("/Login", replayed.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("\"action\":\"logout\"", await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log")), StringComparison.Ordinal);

        using var attacker = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        for (var i = 0; i < DiagnosticsOptions.DefaultLoginMaxFailures; i++)
        {
            await attacker.PostAsync("/Login", Form(await Token(attacker, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "wrong-" + i }));
        }

        var locked = await attacker.PostAsync("/Login", Form(await Token(attacker, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        var lockedHtml = await locked.Content.ReadAsStringAsync();
        Assert.Contains("Too many failed attempts", lockedHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"UnlockCode\"", lockedHtml, StringComparison.Ordinal);

        // The operator reads the unlock code from the console; a guessed code does not help.
        var line = Assert.Single(factory.Announced);
        var code = line[(line.LastIndexOf(' ') + 1)..];
        var guessed = await attacker.PostAsync("/Login", Form(await Token(attacker, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "correct-horse-battery", ["UnlockCode"] = "guess" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, guessed.StatusCode);
        using var operatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var unlocked = await operatorClient.PostAsync("/Login", Form(await Token(operatorClient, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "correct-horse-battery", ["UnlockCode"] = code }));
        Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
        Assert.Contains("login-unlocked", await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_AllowedHosts_RefusesOtherHosts()
    {
        await using var factory = new AppFactory { Reader = new RecordingReader(), AllowedHosts = "redis.internal:6379" };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var setupToken = factory.Services.GetRequiredService<SetupToken>().Value;
        await client.PostAsync("/Setup", Form(await Token(client, "/Setup"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));

        var refused = await client.PostAsync("/", Form(await Token(client, "/"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Host"] = "169.254.169.254", ["Port"] = "6379", ["Database"] = "0", ["ProfileName"] = "session" }));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Host is not in Diagnostics:AllowedHosts.", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var wrongPort = await client.PostAsync("/", Form(await Token(client, "/"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Host"] = "redis.internal", ["Port"] = "6380", ["Database"] = "0", ["ProfileName"] = "session" }));
        Assert.Contains("Host is not in Diagnostics:AllowedHosts.", await wrongPort.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var allowed = await client.PostAsync("/", Form(await Token(client, "/"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Host"] = "redis.internal", ["Port"] = "6379", ["Database"] = "0", ["ProfileName"] = "session" }));
        Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);
        Assert.Contains("connect-refused", await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Factory_SlowConnect_DoesNotBlockOtherSessions()
    {
        using var slowEntered = new ManualResetEventSlim();
        using var releaseSlow = new ManualResetEventSlim();
        using var factory = new StackExchangeExecutorFactory(null, endpoint =>
        {
            if (string.Equals(endpoint.Host, "slow", StringComparison.Ordinal))
            {
                slowEntered.Set();
                releaseSlow.Wait(TimeSpan.FromSeconds(30));
            }

            throw new InvalidOperationException("connect " + endpoint.Host);
        });
        var slow = Task.Run(() => factory.Create(new RedisEndpoint("slow", 6379, string.Empty, string.Empty, false, 0, "session-a")));
        Assert.True(slowEntered.Wait(TimeSpan.FromSeconds(10)));

        // With the connect inside the lock this call would wait for the slow one.
        var fast = Task.Run(() => factory.Create(new RedisEndpoint("fast", 6379, string.Empty, string.Empty, false, 0, "session-b")));
        var finished = await Task.WhenAny(fast, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(fast, finished);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fast);
        Assert.Equal("connect fast", ex.Message);

        releaseSlow.Set();
        await Assert.ThrowsAsync<InvalidOperationException>(() => slow);
    }

    [Fact]
    public async Task Reader_RefusesValuesAboveTheLimit_BeforeGet()
    {
        var executor = new ScriptExecutor();
        var reader = new RedisReader(executor, 3);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync("t:tenant-a:item", CancellationToken.None));
        Assert.Contains("above the limit", ex.Message, StringComparison.Ordinal);
        Assert.Contains("STRLEN", executor.Commands, StringComparer.Ordinal);
        Assert.DoesNotContain("GET", executor.Commands, StringComparer.Ordinal);
        RedisCommandGate.Demand("STRLEN", ["k"]);
    }

    [Fact]
    public void ConnectionKey_CoversThePassword_NotItsLength()
    {
        var good = new RedisEndpoint("h", 6379, "u", "right-password", false, 0, "sid");
        var wrong = new RedisEndpoint("h", 6379, "u", "right-passwurd", false, 0, "sid");
        var same = new RedisEndpoint("h", 6379, "u", "right-password", false, 0, "sid");
        Assert.Equal(good.Password.Length, wrong.Password.Length);
        Assert.False(string.Equals(StackExchangeExecutorFactory.Fingerprint(good), StackExchangeExecutorFactory.Fingerprint(wrong), StringComparison.Ordinal));
        Assert.Equal(StackExchangeExecutorFactory.Fingerprint(good), StackExchangeExecutorFactory.Fingerprint(same));
        Assert.DoesNotContain("right-password", StackExchangeExecutorFactory.Fingerprint(good), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redis_ScanPages_AndCommandMapBlocksKeys()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        var admin = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        var adminDb = admin.GetDatabase();
        for (var i = 0; i < 30; i++)
        {
            await adminDb.StringSetAsync($"t:tenant-a:item:{i}", "v");
        }

        var parts = container.GetConnectionString().Split(':');
        var endpoint = new RedisEndpoint(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), string.Empty, string.Empty, false, 0, "sid");
        using var factory = new StackExchangeExecutorFactory();
        var calls = new List<string>();
        var options = ConfigurationOptions.Parse(container.GetConnectionString());
        options.CommandMap = StackExchangeRedisExecutor.DenyDangerousCommands();
        var mux = await ConnectionMultiplexer.ConnectAsync(options);
        var executor = new StackExchangeRedisExecutor(mux.GetDatabase(), calls.Add);
        var reader = new RedisReader(executor, 1024);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = "0";
        do
        {
            var page = await reader.ScanAsync("t:tenant-a:*", 10, cursor, CancellationToken.None);
            foreach (var row in page.Rows)
            {
                seen.Add(row.Key);
            }

            cursor = page.NextCursor;
        }
        while (cursor != "0");

        Assert.Equal(30, seen.Count);
        Assert.Contains("SCAN", calls, StringComparer.Ordinal);
        Assert.DoesNotContain("KEYS", calls, StringComparer.Ordinal);
        Assert.DoesNotContain("SET", calls, StringComparer.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync("SET", ["k", "v"], CancellationToken.None));
        var denied = await Assert.ThrowsAsync<RedisCommandException>(() => mux.GetDatabase().ExecuteAsync("KEYS", "*"));
        Assert.Contains("KEYS", denied.Message, StringComparison.Ordinal);
        await mux.DisposeAsync();
        await admin.DisposeAsync();
        _ = factory;
        _ = endpoint;
    }

    private static FormUrlEncodedContent Form(string? token, Dictionary<string, string> fields)
    {
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(fields);
    }

    private static async Task<string> Token(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        }

        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var nested = Path.Combine(dir.FullName, "src", Path.GetFileNameWithoutExtension(fileName), fileName);
            if (File.Exists(nested))
            {
                return nested;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }

    private sealed class FlagExecutor : IRedisExecutor
    {
        private readonly Action called;

        internal FlagExecutor(Action called)
        {
            this.called = called;
        }

        public Task<RedisBulk> ExecuteAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            called();
            throw new InvalidOperationException(command);
        }
    }

    private sealed class ScriptExecutor : IRedisExecutor
    {
        internal List<string> Commands { get; } = [];

        public Task<RedisBulk> ExecuteAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command == "SCAN")
            {
                return Task.FromResult(new RedisBulk
                {
                    Items =
                    [
                        new RedisBulk { Text = "0" },
                        new RedisBulk { Items = [new RedisBulk { Text = "t:tenant-a:item" }] },
                    ],
                });
            }

            if (command == "GET")
            {
                return Task.FromResult(new RedisBulk { Bytes = Encoding.UTF8.GetBytes("hello"), Text = "hello" });
            }

            if (command == "TTL")
            {
                return Task.FromResult(new RedisBulk { Integer = -1 });
            }

            return Task.FromResult(new RedisBulk { Text = "string", Integer = 4 });
        }
    }

    private sealed class RecordingReader : ICacheReader
    {
        internal List<string> Scans { get; } = [];

        public Task<ScanPage> ScanAsync(RedisEndpoint endpoint, string pattern, int pageSize, string cursor, CancellationToken cancellationToken)
        {
            Scans.Add(pattern);
            return Task.FromResult(new ScanPage("0", []));
        }

        public Task<KeyRead> ReadAsync(RedisEndpoint endpoint, string key, CancellationToken cancellationToken)
        {
            if (key == "big-key")
            {
                throw new InvalidOperationException("Value is 2000000 bytes, above the limit of 1048576 bytes.");
            }

            return Task.FromResult(new KeyRead(key, "string", Encoding.UTF8.GetBytes("PAYLOADSECRET"), string.Empty, "tenant-a"));
        }
    }

    private sealed class AppFactory : WebApplicationFactory<Program>
    {
        internal string DataDir { get; } = Path.Combine(Path.GetTempPath(), "gocgun-cache-" + Guid.NewGuid().ToString("N"));

        internal ICacheReader? Reader { get; init; }

        internal string? AllowedHosts { get; init; }

        internal List<string> Announced { get; } = [];

        internal CollectingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(DataDir);
            builder.UseSetting("Diagnostics:DataDirectory", DataDir);
            builder.UseSetting("urls", "http://127.0.0.1:0");
            if (AllowedHosts is not null)
            {
                builder.UseSetting("Diagnostics:AllowedHosts", AllowedHosts);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LoginThrottle>();
                services.AddSingleton(provider => new LoginThrottle(provider.GetRequiredService<DiagnosticsOptions>(), provider.GetRequiredService<TimeProvider>(), Announced.Add));
            });
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(Logs);
            });
            if (Reader is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICacheReader>();
                    services.AddSingleton(Reader);
                });
            }
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        internal List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger(Lines);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> lines;

        internal CollectingLogger(List<string> lines)
        {
            this.lines = lines;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Add(formatter(state, exception));
        }
    }
}
