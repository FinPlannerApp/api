namespace Infrastructure.BackgroundJobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class PainVelocitySchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PainVelocitySchedulerWorker> _logger;

    public PainVelocitySchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<PainVelocitySchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs once daily at 3:00 AM UTC — a derived display/sorting
        // metric, not time-sensitive in any real sense; daily
        // recalculation is genuinely sufficient. Deliberately a
        // different hour than the recurring-transaction worker (1 AM)
        // and the token cleanup worker (2 AM), so all three don't fire
        // in the same window.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 3 ? 1 : 0).AddHours(3);
            var delay = nextRun - now;

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<UpdatePainVelocityJob>();
                await job.UpdateVelocitiesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pain velocity update failed.");
            }
        }
    }
}