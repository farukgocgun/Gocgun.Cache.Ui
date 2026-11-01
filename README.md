# Gocgun.Cache.Ui

> **Beta.** This package is in beta: the public API may change between releases.
>
> It references `Gocgun.Diagnostics.Shared` through a sibling `ProjectReference`; the base package repository must sit next to this one to build. Publish order: `Gocgun.Diagnostics.Shared` first.

Read-only Redis diagnostic tool. Command name: `gocgun-cache-ui`.

It binds to `127.0.0.1` on a free port unless `--urls` is set. `--urls` must name `127.0.0.1` or another loopback host. Login uses `PasswordHasher` and does not reference `Gocgun.Identity`. Redis access is `SCAN`, `TYPE`, `TTL`, `MEMORY USAGE`, `GET`, `HSCAN`, `LRANGE` (at most 100), `SSCAN`, and `ZSCAN`. `KEYS` and every write command are rejected before they are sent.

First run: the console prints a one-time setup token. `/Setup` needs it; it is not shown once a password exists. Sign-in is a non-persistent cookie that expires after `Diagnostics:SessionHours` (default 8). The Log out button posts to `/Logout`, which drops the in-memory session and invalidates the cookie. After `Diagnostics:LoginMaxFailures` (5) failed logins the login is locked for `Diagnostics:LoginLockoutSeconds` (300). Any local process can trigger that lock, so each lockout prints a one-time unlock code to the console; the password plus that code logs in during the lock. Expired sessions are dropped by a background sweep every `Diagnostics:SessionSweepSeconds` (60), not only when a request arrives. The tool stops itself if the server ends up bound to a non-loopback address. `admin.hash`, profile and mapping files are mode 600 on Unix. Data Protection keys (`keys/` in `Diagnostics:DataDirectory`) are stored unencrypted; protect that folder with OS permissions. Login, failed login, logout and contract loads are audited (never passwords). Profile names allow `[A-Za-z0-9._-]` up to 64 characters; Windows device names (`CON`, `NUL`, `COM1`, ...) before the first dot are refused on every OS.

`Diagnostics:AllowedHosts` (optional) limits the connection form to listed hosts: a comma-separated string or an array, each entry `host`, `host:port` or `[ipv6]:port`. Empty allows any host. Matching is on the typed text, not the resolved address, so list names you trust to resolve correctly. Refusals are audited as `connect-refused`.

Contract assemblies load only from below `Diagnostics:ContractRoot`; without it loading is refused. A folder that passes through a symbolic link or junction below the root is refused, and linked `.dll` files are skipped. String values larger than `Diagnostics:MaxValueBytes` (default 1048576, checked with `STRLEN`) are not read.

License: AGPL-3.0-only (`LICENSE`). Copyright holder exception: `LICENSE-EXCEPTION`.

```bash
dotnet run --project src/Gocgun.Cache.Ui/Gocgun.Cache.Ui.csproj
```

## Publishing to nuget.org

Run `scripts/publish.sh` (or `scripts/publish.ps1` on Windows). It dispatches the manual `release.yml` workflow of each dependency first, in dependency order (Gocgun.Diagnostics.Shared, Gocgun.Cache.Ui), then of this package, waiting for each run and stopping on the first failure. Requires the GitHub CLI signed in with `gh auth login`; the `nuget` environment and the nuget.org Trusted Publishing policy must exist in each repository.
