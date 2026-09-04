using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

public class AccountChargeAdvanceSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountChargeAdvanceSchedulerWorker> _logger;

    public AccountChargeAdvanceSchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<AccountChargeAdvanceSchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 5 AM UTC — after the other four daily workers (1, 2, 3, 4 AM).
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 5 ? 1 : 0).AddHours(5);
            var delay = nextRun - now;

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<AccountChargeAdvanceJob>();
                await job.AdvancePastDueChargesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Account charge date advance failed.");
            }
        }
    }
}
