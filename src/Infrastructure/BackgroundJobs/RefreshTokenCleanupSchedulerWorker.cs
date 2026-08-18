namespace Infrastructure.BackgroundJobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class RefreshTokenCleanupSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupSchedulerWorker> _logger;

    public RefreshTokenCleanupSchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupSchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 2 ? 1 : 0).AddHours(2); // next 2:00 AM UTC
            var delay = nextRun - now;

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupJob>();
                await job.CleanupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh token cleanup failed.");
            }
        }
    }
}