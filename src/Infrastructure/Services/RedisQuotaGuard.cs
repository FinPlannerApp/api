using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Counts the Redis commands this app issues and protects an Upstash command quota.
///
///   * Every Redis call site records itself here (cheap, in-memory).
///   * Once today's count reaches the DAILY budget, <see cref="OptionalAllowed"/> turns false:
///     optional work (L2 cache reads/writes) is skipped and the app serves from memory + the
///     database until the next UTC day. Essential work (queuing an email, OTP, cache deletes)
///     keeps going.
///   * Admins can read the numbers at GET /api/admin/redis-usage.
///
/// Budget:  Redis:MonthlyCommandBudget (default 500 000 — your Upstash plan's monthly limit),
///          x 80% safety margin, spread over 30 days. (PING / AUTH / INFO etc. are not billed by Upstash.)
/// Counts are per process and reset on restart; Upstash's dashboard remains the source of truth.
/// </summary>
public sealed class RedisQuotaGuard
{
    private readonly ILogger<RedisQuotaGuard> _logger;
    private readonly ConcurrentDictionary<string, long> _byCategory = new(StringComparer.Ordinal);
    private readonly object _rollLock = new();
    private readonly long _monthlyBudget;
    private readonly long _dailyBudget;

    private DateOnly _day;
    private long _total;
    private bool _warned80;
    private bool _warned100;

    public RedisQuotaGuard(IConfiguration configuration, ILogger<RedisQuotaGuard> logger)
    {
        _logger = logger;
        _monthlyBudget = configuration.GetValue<long?>("Redis:MonthlyCommandBudget") ?? 500_000;

        var perDay = (long)(_monthlyBudget * 0.8 / 30.0);
        _dailyBudget = Math.Max(1_000, perDay);
    }

    /// <summary>True while today's command count is under the daily budget.</summary>
    public bool OptionalAllowed
    {
        get
        {
            RollDay();
            return Interlocked.Read(ref _total) < _dailyBudget;
        }
    }

    public void Record(string category, int commands = 1)
    {
        RollDay();
        _byCategory.AddOrUpdate(category, commands, (_, old) => old + commands);
        var total = Interlocked.Add(ref _total, commands);

        if (!_warned80 && total >= _dailyBudget * 0.8)
        {
            _warned80 = true;
            _logger.LogWarning("[Redis quota] 80% of today's budget used ({Total}/{Budget}).", total, _dailyBudget);
        }
        if (!_warned100 && total >= _dailyBudget)
        {
            _warned100 = true;
            _logger.LogWarning("[Redis quota] Daily budget reached ({Total}/{Budget}). Optional Redis use is paused until tomorrow (UTC).", total, _dailyBudget);
        }
    }

    public object Snapshot()
    {
        RollDay();
        var total = Interlocked.Read(ref _total);
        var elapsedHours = Math.Max(1.0, DateTime.UtcNow.TimeOfDay.TotalHours);
        var projectedDaily = (long)(total / elapsedHours * 24);

        return new
        {
            dayUtc = _day.ToString("yyyy-MM-dd"),
            commandsToday = total,
            dailyBudget = _dailyBudget,
            percentOfDailyBudget = Math.Round(total * 100.0 / _dailyBudget, 1),
            optionalRedisUse = total < _dailyBudget ? "on" : "paused",
            projectedCommandsPerDay = projectedDaily,
            projectedMonthly = projectedDaily * 30,
            monthlyBudget = _monthlyBudget,
            byCategory = _byCategory.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            note = "Counts this process only (resets on restart). Upstash's dashboard is the source of truth."
        };
    }

    private void RollDay()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == _day) return;
        lock (_rollLock)
        {
            if (today == _day) return;
            _day = today;
            Interlocked.Exchange(ref _total, 0);
            _byCategory.Clear();
            _warned80 = false;
            _warned100 = false;
        }
    }
}
