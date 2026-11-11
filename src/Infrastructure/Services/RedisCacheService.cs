using Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace Infrastructure.Services;

/// <summary>
/// Two-tier cache optimised for Upstash free tier (limited daily commands):
///
///   L1 = IMemoryCache  → in-process, zero Redis commands, instant
///   L2 = Redis/Upstash → distributed, used only on L1 miss
///
/// Command budget breakdown per cache operation:
///   GET  → 0 Redis commands if L1 hit (common case), 1 GET if miss
///   SET  → 1 SET command (L2) + 0 (L1 is free)
///   DEL  → 1 DEL command
///
/// The cache is memory-only by default (Cache:UseRedis = false): Redis is reserved for OTP /
/// verification codes, so caching costs ZERO Redis commands — reads, writes and invalidation all
/// happen in process (the Redis connection is not even opened for it). Set Cache:UseRedis=true
/// only if you run several app instances that must share cached data.
///
/// Optional L2 reads/writes are skipped while <see cref="RedisQuotaGuard"/> says today's
/// command budget is used up.
///
/// Values are gzip-compressed before writing to Redis to stay within
/// Upstash storage limits and reduce bandwidth.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IMemoryCache _l1;
    private readonly Lazy<IConnectionMultiplexer> _muxer;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly RedisQuotaGuard _guard;
    private readonly bool _useRedis;

    // Resolved only when Redis is actually used — with Cache:UseRedis=false (default) the
    // connection is never opened by the cache.
    private IDatabase _db => _muxer.Value.GetDatabase();
    private IServer? _server
    {
        get
        {
            var m = _muxer.Value;
            var endpoints = m.GetEndPoints();
            return endpoints.Any() ? m.GetServer(endpoints.First()) : null;
        }
    }

    // After an L2 hit, keep the value in memory this long so repeated reads don't each cost a GET.
    private static readonly TimeSpan L2BackfillTtl = TimeSpan.FromMinutes(5);

    // L1 TTL is intentionally shorter than L2 so stale reads are rare
    private static readonly TimeSpan L1OverheadFactor = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public RedisCacheService(
        IMemoryCache l1,
        IServiceProvider services,
        ILogger<RedisCacheService> logger,
        IConfiguration configuration,
        RedisQuotaGuard guard)
    {
        _guard = guard;
        _useRedis = configuration.GetValue<bool>("Cache:UseRedis");
        _l1 = l1;
        _logger = logger;
        _muxer = new Lazy<IConnectionMultiplexer>(() => services.GetRequiredService<IConnectionMultiplexer>());

        _logger.LogInformation("[Cache] Mode: {Mode}", _useRedis ? "memory + Redis (L2)" : "memory only — Redis not used by the cache");
    }

    // ── Versioned namespaces ───────────────────────────────────────────────
    // Keys shaped "dash::{userId}:rest" are stored as "dash::{userId}:v{N}:rest".
    // Invalidating a user = bumping N (ONE Redis command, no SCAN, no per-key DEL).
    // Old entries become unreachable immediately and simply expire by TTL.
    // N lives in process memory (free) and in Redis ("ver::<scope>") so it survives
    // restarts; the in-memory copy is re-read from Redis every VersionRecheck so a
    // second instance would pick up the other's invalidations.
    private readonly ConcurrentDictionary<string, (long Ver, DateTime CheckedAt)> _versions = new();
    private static readonly TimeSpan VersionRecheck = TimeSpan.FromSeconds(30);
    private const string VersionedRoot = "dash::";

    /// <summary>True when the cache must not touch Redis at all (the default).</summary>
    private bool IsMemoryOnly(string originalKey) => !_useRedis;

    private static bool TryGetScope(string key, out string scope)
    {
        scope = string.Empty;
        if (!key.StartsWith(VersionedRoot, StringComparison.Ordinal)) return false;
        var idx = key.IndexOf(':', VersionedRoot.Length);
        if (idx < 0) return false;
        scope = key[..(idx + 1)]; // "dash::{userId}:"
        return true;
    }

    // Version each read started under, keyed by the caller's original key. A value computed
    // after a cache miss is written back under THAT version, so if an invalidation lands while
    // it was being computed, the (possibly stale) result goes to the old, unreachable version
    // instead of poisoning the new one.
    private readonly ConcurrentDictionary<string, long> _readVersions = new();

    private async Task<string> ResolveKeyAsync(string key, bool forRead)
    {
        if (!TryGetScope(key, out var scope)) return key;

        long ver;
        if (forRead)
        {
            ver = await GetVersionAsync(scope);
            if (_readVersions.Count > 10_000) _readVersions.Clear();
            _readVersions[key] = ver;
        }
        else
        {
            ver = _readVersions.TryRemove(key, out var seen) ? seen : await GetVersionAsync(scope);
        }
        return $"{scope}v{ver}:{key[scope.Length..]}";
    }

    private async Task<long> GetVersionAsync(string scope)
    {
        // Memory-only mode: the version lives purely in this process (a restart also empties
        // the memory cache, so nothing can go stale). Zero Redis commands.
        if (!_useRedis)
            return _versions.TryGetValue(scope, out var local) ? local.Ver : 0;

        var now = DateTime.UtcNow;
        _versions.TryGetValue(scope, out var entry); // default = (0, MinValue) when absent
        if (entry.CheckedAt != default && now - entry.CheckedAt < VersionRecheck)
            return entry.Ver;

        var ver = entry.Ver;
        try
        {
            if (_db.Multiplexer.IsConnected)
            {
                _guard.Record("cache-version-get");
                var stored = await _db.StringGetAsync($"ver::{scope}");
                if (stored.HasValue && long.TryParse((string?)stored, out var parsed))
                    ver = Math.Max(ver, parsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Version read failed for {Scope}. Using local version.", scope);
        }

        _versions.AddOrUpdate(scope, (ver, now), (_, old) => (Math.Max(old.Ver, ver), now));
        return ver;
    }

    private async Task BumpVersionAsync(string scope)
    {
        var now = DateTime.UtcNow;
        var next = _versions.TryGetValue(scope, out var e) ? e.Ver + 1 : 1;

        // Memory-only mode: just bump the local number. Free, instant, no Redis involved.
        if (!_useRedis)
        {
            _versions.AddOrUpdate(scope, (next, now), (_, old) => (Math.Max(old.Ver, next), now));
            return;
        }

        try
        {
            if (_db.Multiplexer.IsConnected)
            {
                _guard.Record("cache-version-bump");
                var fromRedis = await _db.StringIncrementAsync($"ver::{scope}");
                next = Math.Max(next, fromRedis);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Version bump failed in Redis for {Scope}. Local only.", scope);
        }

        // Local copy updates immediately, so this instance never serves old data.
        _versions.AddOrUpdate(scope, (next, now), (_, old) => (Math.Max(old.Ver, next), now));
        _logger.LogDebug("[Cache BUMP] {Scope} -> v{Ver}", scope, next);
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key)
    {
        var originalKey = key;
        key = await ResolveKeyAsync(key, forRead: true);

        // ── L1 check (zero Upstash commands) ──────────────────────────────
        if (_l1.TryGetValue(key, out T? l1Value))
        {
            _logger.LogDebug("[Cache L1-HIT] {Key}", key);
            _readVersions.TryRemove(originalKey, out _);
            return l1Value;
        }

        // ── L2 check (1 Upstash command) — skipped for memory-only keys and while the
        //    daily command budget is exhausted.
        if (IsMemoryOnly(originalKey) || !_guard.OptionalAllowed || _db.Multiplexer.IsConnected == false)
        {
            return default;
        }

        try
        {
            _guard.Record("cache-get");
            var compressed = await _db.StringGetAsync(key);
            if (compressed.IsNullOrEmpty)
            {
                _logger.LogDebug("[Cache MISS] {Key}", key);
                return default;
            }

            var json = Decompress((byte[])compressed!);
            var value = JsonSerializer.Deserialize<T>(json, _json);

            // Backfill L1 so the next calls don't hit Redis
            _l1.Set(key, value, L2BackfillTtl);

            _logger.LogDebug("[Cache L2-HIT] {Key}", key);
            _readVersions.TryRemove(originalKey, out _);
            return value;
        }
        catch (Exception ex)
        {
            // Redis unavailable → graceful degradation (caller fetches from DB)
            _logger.LogWarning(ex, "[Cache] Redis GET failed for {Key}. Falling back to DB.", key);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        var originalKey = key;
        key = await ResolveKeyAsync(key, forRead: false);

        // ── L1 write (free, short-lived) ──────────────────────────────────
        var l1Ttl = expiry > L1OverheadFactor ? expiry - L1OverheadFactor : expiry;
        _l1.Set(key, value, l1Ttl);

        // ── L2 write — gzip compress to save Upstash storage (1 command) ──
        if (IsMemoryOnly(originalKey) || !_guard.OptionalAllowed || _db.Multiplexer.IsConnected == false)
        {
            return;
        }

        try
        {
            _guard.Record("cache-set");
            var json = JsonSerializer.Serialize(value, _json);
            var compressed = Compress(json);
            await _db.StringSetAsync(key, compressed, expiry);
            _logger.LogDebug("[Cache SET] {Key} TTL={Expiry}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Redis SET failed for {Key}. L1 only.", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key)
    {
        var originalKey = key;
        key = await ResolveKeyAsync(key, forRead: false);
        _l1.Remove(key);

        // Deletes stay on even when the daily budget is used up: skipping one would let a
        // stale value survive in Redis.
        if (IsMemoryOnly(originalKey)) return;

        try
        {
            _guard.Record("cache-del");
            await _db.KeyDeleteAsync(key);
            _logger.LogDebug("[Cache DEL] {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Redis DEL failed for {Key}.", key);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// For versioned namespaces ("dash::{userId}:") this is a single version bump — no SCAN,
    /// no DEL, immediate for readers. Any other prefix falls back to SCAN, deleting the matches
    /// with ONE batched DEL instead of one command per key.
    /// </remarks>
    public async Task RemoveByPrefixAsync(string prefix)
    {
        if (TryGetScope(prefix, out var scope) && scope == prefix)
        {
            await BumpVersionAsync(scope);
            return;
        }

        try
        {
            if (_server == null) return;
            var keys = new List<RedisKey>();
            await foreach (var key in _server.KeysAsync(pattern: $"{prefix}*"))
            {
                _l1.Remove((string)key!);
                keys.Add(key);
            }
            _guard.Record("cache-scan");
            if (keys.Count > 0)
            {
                _guard.Record("cache-del");
                await _db.KeyDeleteAsync(keys.ToArray());
            }
            _logger.LogDebug("[Cache DEL-PREFIX] {Prefix}* ({Count} keys)", prefix, keys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Redis prefix-delete failed for {Prefix}.", prefix);
        }
    }

    // ── Compression helpers ────────────────────────────────────────────────

    private static byte[] Compress(string text)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest))
            gz.Write(inputBytes, 0, inputBytes.Length);
        return output.ToArray();
    }

    private static string Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        return reader.ReadToEnd();
    }
}
