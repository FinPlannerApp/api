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
        await Task.Delay(20000, stoppingToken); // stagger slightly from the other worker's startup delay

        while (!stoppingToken.IsCancellationRequested)
        {
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

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}