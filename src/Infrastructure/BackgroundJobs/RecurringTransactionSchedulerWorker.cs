namespace Infrastructure.BackgroundJobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class RecurringTransactionSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTransactionSchedulerWorker> _logger;

    public RecurringTransactionSchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<RecurringTransactionSchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs once daily at 1:00 AM UTC (~6:30 AM IST) — recurring
        // transactions only ever need daily granularity at most (no
        // Frequency option is more frequent than Daily), and this
        // records something that already happened financially, not
        // something time-sensitive being triggered live.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 1 ? 1 : 0).AddHours(1);
            var delay = nextRun - now;

            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<RecurringTransactionJob>();
                await job.ProcessRecurringTransactionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring transaction processing failed.");
            }
        }
    }
}