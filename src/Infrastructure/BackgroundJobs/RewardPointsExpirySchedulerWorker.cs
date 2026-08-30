using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

public class RewardPointsExpirySchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RewardPointsExpirySchedulerWorker> _logger;

    public RewardPointsExpirySchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<RewardPointsExpirySchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs once daily at 4:00 AM UTC — after the other three
        // existing daily workers (1, 2, 3 AM), avoiding a collision.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 4 ? 1 : 0).AddHours(4);
            var delay = nextRun - now;

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<RewardPointsExpiryJob>();
                await job.ExpirePointsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reward points expiry check failed.");
            }
        }
    }
}

