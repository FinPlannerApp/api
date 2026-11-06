using Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
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
/// Values are gzip-compressed before writing to Redis to stay within
/// Upstash storage limits and reduce bandwidth.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IMemoryCache _l1;
    private readonly IDatabase _db;
    private readonly IServer? _server;
    private readonly ILogger<RedisCacheService> _logger;

    // L1 TTL is intentionally shorter than L2 so stale reads are rare
    private static readonly TimeSpan L1OverheadFactor = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public RedisCacheService(
        IMemoryCache l1,
        IConnectionMultiplexer muxer,
        ILogger<RedisCacheService> logger)
    {
        _l1 = l1;
        _db = muxer.GetDatabase();
        _logger = logger;

        // GetServer is needed for pattern-delete (SCAN). Handle offline scenario.
        var endpoints = muxer.GetEndPoints();
        if (endpoints.Any())
        {
            _server = muxer.GetServer(endpoints.First());
        }
        else
        {
            _logger.LogWarning("[Cache] No Redis endpoints available. L2 features will be disabled.");
        }
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
        var now = DateTime.UtcNow;
        _versions.TryGetValue(scope, out var entry); // default = (0, MinValue) when absent
        if (entry.CheckedAt != default && now - entry.CheckedAt < VersionRecheck)
            return entry.Ver;

        var ver = entry.Ver;
        try
        {
            if (_db.Multiplexer.IsConnected)
            {
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
        try
        {
            if (_db.Multiplexer.IsConnected)
            {
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

        // ── L2 check (1 Upstash command) ──────────────────────────────────
        if (_db.Multiplexer.IsConnected == false)
        {
            return default;
        }

        try
        {
            var compressed = await _db.StringGetAsync(key);
            if (compressed.IsNullOrEmpty)
            {
                _logger.LogDebug("[Cache MISS] {Key}", key);
                return default;
            }

            var json = Decompress((byte[])compressed!);
            var value = JsonSerializer.Deserialize<T>(json, _json);

            // Backfill L1 so the next call doesn't hit Redis
            _l1.Set(key, value, L1OverheadFactor);

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
        key = await ResolveKeyAsync(key, forRead: false);

        // ── L1 write (free, short-lived) ──────────────────────────────────
        var l1Ttl = expiry > L1OverheadFactor ? expiry - L1OverheadFactor : expiry;
        _l1.Set(key, value, l1Ttl);

        // ── L2 write — gzip compress to save Upstash storage (1 command) ──
        if (_db.Multiplexer.IsConnected == false)
        {
            return;
        }

        try
        {
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
        key = await ResolveKeyAsync(key, forRead: false);
        _l1.Remove(key);
        try
        {
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
            if (keys.Count > 0)
                await _db.KeyDeleteAsync(keys.ToArray());
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
