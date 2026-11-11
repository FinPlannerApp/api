using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Upstash archives a free database after about 30 days with no activity. Redis is now used only
/// for OTP codes, so a quiet month (nobody registering or resetting a password) could get it
/// archived and break the next OTP. One tiny write every few days counts as activity.
/// Redis:KeepDatabaseActiveDays (default 7; 0 turns this off).
/// </summary>
public sealed class RedisActivityKeeperWorker : BackgroundService
{
    private readonly Lazy<IConnectionMultiplexer> _redis;
    private readonly ILogger<RedisActivityKeeperWorker> _logger;
    private readonly int _everyDays;

    public RedisActivityKeeperWorker(
        Lazy<IConnectionMultiplexer> redis,
        IConfiguration configuration,
        ILogger<RedisActivityKeeperWorker> logger)
    {
        _redis = redis;
        _logger = logger;
        _everyDays = Math.Min(20, configuration.GetValue<int?>("Redis:KeepDatabaseActiveDays") ?? 7); // Task.Delay tops out near 24 days
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_everyDays <= 0) return;

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); // let the app finish starting

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A real write (PING is not counted as activity by every provider). ~1 command a week.
                    await _redis.Value.GetDatabase().StringSetAsync("keepalive:last", DateTime.UtcNow.ToString("O"), TimeSpan.FromDays(_everyDays * 3));
                    _logger.LogInformation("Redis activity touch done (every {Days} days).", _everyDays);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Redis activity touch failed; will retry next cycle.");
                }

                await Task.Delay(TimeSpan.FromDays(_everyDays), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
