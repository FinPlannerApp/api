using Application.Contracts;
using Application.DTOs.Auth;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Infrastructure.Services;

namespace Infrastructure.BackgroundJobs;

public class InfrastructureMonitorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InfrastructureMonitorWorker> _logger;
    private readonly IConfiguration _configuration;
    private bool _redisLastState = true;
    private bool _dbLastState = true;
    private DateTime _lastRedisCheckUtc = DateTime.MinValue;

    public InfrastructureMonitorWorker(
        IServiceProvider serviceProvider, 
        ILogger<InfrastructureMonitorWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InfrastructureMonitorWorker started.");

        // Wait a bit for other services to initialize
        await Task.Delay(10000, stoppingToken);

        // Genuinely configurable now, not just a comment claiming to be —
        // defaults to 60 minutes if unset. At 5 minutes, this check was
        // landing exactly at Neon's scale-to-zero idle threshold, meaning
        // the database's 5-minute idle window never completed and it
        // never actually suspended — burning through the entire 100
        // CU-hour free-tier budget on its own, before any real usage.
        // At 60 minutes, the database gets ~55 minutes of genuine idle
        // time between checks, letting scale-to-zero actually work,
        // while still catching an outage within an hour.
        var intervalMinutes = _configuration.GetValue<int?>("Infrastructure:HealthCheckIntervalMinutes") ?? 60;
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckHealthAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task CheckHealthAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IDirectEmailSender>();

        // 1. Check Redis (OTP codes depend on it). A genuine round trip - write a key, read it back -
        // not IsConnected, which can be true on a dead or unauthorised connection. Runs at most once
        // per Infrastructure:RedisCheckIntervalHours (default 24), so it costs ~60 commands a month.
        // Set Infrastructure:CheckRedis=false to turn it off.
        var checkRedis = _configuration.GetValue<bool?>("Infrastructure:CheckRedis") ?? true;
        var redisEvery = TimeSpan.FromHours(_configuration.GetValue<int?>("Infrastructure:RedisCheckIntervalHours") ?? 24);
        var redisDue = checkRedis && DateTime.UtcNow - _lastRedisCheckUtc >= redisEvery;

        if (redisDue)
        {
            _lastRedisCheckUtc = DateTime.UtcNow;
            bool redisCurrentState;
            try
            {
                var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
                var db = redis.GetDatabase();
                var probe = Guid.NewGuid().ToString("N");
                await db.StringSetAsync("health:probe", probe, TimeSpan.FromHours(48));
                redisCurrentState = (await db.StringGetAsync("health:probe")).ToString() == probe;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis health probe failed.");
                redisCurrentState = false;
            }

            if (redisCurrentState != _redisLastState)
            {
                if (!redisCurrentState)
                {
                    _logger.LogCritical("CRITICAL: Redis is not working (OTP codes will fail)!");
                    await SendAlertAsync(emailSender, "CRITICAL: Redis Down", "A Redis write/read test failed. Registration and password-reset OTP codes will not work until Redis is back (check the Upstash database - it may be archived or over quota).");
                }
                else
                {
                    _logger.LogInformation("RECOVERY: Redis is working again.");
                    await SendAlertAsync(emailSender, "RECOVERY: Redis Restored", "The Redis write/read test passed again. OTP codes work.");
                }
                _redisLastState = redisCurrentState;
            }
        }

        // 2. Check Database
        bool dbCurrentState = false;
        try
        {
            dbCurrentState = await dbContext.Database.CanConnectAsync();
        }
        catch { dbCurrentState = false; }

        if (dbCurrentState != _dbLastState)
        {
            if (!dbCurrentState)
            {
                _logger.LogCritical("CRITICAL: Database is DOWN!");
                await SendAlertAsync(emailSender, "CRITICAL: Database Infrastructure Down", "The PostgreSQL database is unreachable. The application is likely non-functional.");
            }
            else
            {
                _logger.LogInformation("RECOVERY: Database is back online.");
                await SendAlertAsync(emailSender, "RECOVERY: Database Infrastructure Restored", "The PostgreSQL database is back online.");
            }
            _dbLastState = dbCurrentState;
        }
    }

    private async Task SendAlertAsync(IDirectEmailSender emailSender, string subject, string message)
    {
        var alertEmail = _configuration["Infrastructure:AlertEmail"];
        if (string.IsNullOrEmpty(alertEmail))
        {
            _logger.LogWarning("Alert email address not configured. Skipping alert email for: {Subject}", subject);
            return;
        }

        var mailRequest = new MailRequest
        {
            To = alertEmail,
            Subject = subject,
            Body = $"<h2>Infrastructure Alert</h2><p>{message}</p><p>Time: {DateTime.UtcNow} UTC</p>"
        };

        await emailSender.SendEmailAsync(mailRequest);
    }
}
