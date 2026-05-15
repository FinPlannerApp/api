using Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
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

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key)
    {
        // ── L1 check (zero Upstash commands) ──────────────────────────────
        if (_l1.TryGetValue(key, out T? l1Value))
        {
            _logger.LogDebug("[Cache L1-HIT] {Key}", key);
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
    /// Uses SCAN (cursor-based) instead of KEYS to avoid blocking Redis on large keyspaces.
    /// L1 entries with matching keys expire naturally within L1OverheadFactor seconds.
    /// </remarks>
    public async Task RemoveByPrefixAsync(string prefix)
    {
        try
        {
            if (_server == null) return;
            var pattern = $"{prefix}*";
            await foreach (var key in _server.KeysAsync(pattern: pattern))
            {
                _l1.Remove((string)key!);
                await _db.KeyDeleteAsync(key);
            }
            _logger.LogDebug("[Cache DEL-PREFIX] {Prefix}*", prefix);
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
