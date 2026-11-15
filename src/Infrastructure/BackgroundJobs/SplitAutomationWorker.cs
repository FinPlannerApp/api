namespace Infrastructure.BackgroundJobs;

using Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hourly: adds split expenses that are due (rent, subscriptions ...) and sends weekly payment
/// reminders for groups that opted in. Both jobs claim their work in the database first, so
/// running on more than one server never duplicates an expense or an email.
/// </summary>
public class SplitAutomationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SplitAutomationWorker> _logger;

    public SplitAutomationWorker(IServiceScopeFactory scopeFactory, ILogger<SplitAutomationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var automation = scope.ServiceProvider.GetRequiredService<ISplitAutomationService>();
                var added = await automation.RunDueRecurringAsync(DateTime.UtcNow);
                var reminders = await automation.SendAutoRemindersAsync(DateTime.UtcNow);
                if (added > 0 || reminders > 0)
                    _logger.LogInformation("Split automation: {Added} recurring expense(s) added, {Reminders} reminder(s) sent.", added, reminders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Split automation run failed.");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
