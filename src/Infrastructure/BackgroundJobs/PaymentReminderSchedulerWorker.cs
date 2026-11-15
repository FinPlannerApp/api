using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>Runs the payment-reminder digest once a day at 03:30 UTC (09:00 IST).</summary>
public class PaymentReminderSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentReminderSchedulerWorker> _logger;

    public PaymentReminderSchedulerWorker(IServiceScopeFactory scopeFactory, ILogger<PaymentReminderSchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(3).AddMinutes(30);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            await Task.Delay(nextRun - now, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<PaymentReminderJob>();
                await job.RunAsync(ct: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Payment reminder run failed.");
            }
        }
    }
}
