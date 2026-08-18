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
        await Task.Delay(15000, stoppingToken); // let the app finish starting up first

        while (!stoppingToken.IsCancellationRequested)
        {
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

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}