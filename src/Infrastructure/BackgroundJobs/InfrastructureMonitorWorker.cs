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

            // Check every 5 minutes (configurable)
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CheckHealthAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var redis = scope.ServiceProvider.GetService<IConnectionMultiplexer>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IDirectEmailSender>();

        // 1. Check Redis
        bool redisCurrentState = false;
        if (redis != null)
        {
            try
            {
                redisCurrentState = redis.IsConnected;
                if (!redisCurrentState)
                {
                    // Try a ping to be sure
                    var db = redis.GetDatabase();
                    await db.PingAsync();
                    redisCurrentState = true;
                }
            }
            catch { redisCurrentState = false; }
        }

        if (redisCurrentState != _redisLastState)
        {
            if (!redisCurrentState)
            {
                _logger.LogCritical("CRITICAL: Redis server is DOWN!");
                await SendAlertAsync(emailSender, "CRITICAL: Redis Infrastructure Down", "The Redis server is unreachable. Fallback mechanisms are active.");
            }
            else
            {
                _logger.LogInformation("RECOVERY: Redis server is back online.");
                await SendAlertAsync(emailSender, "RECOVERY: Redis Infrastructure Restored", "The Redis server is back online.");
            }
            _redisLastState = redisCurrentState;
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
