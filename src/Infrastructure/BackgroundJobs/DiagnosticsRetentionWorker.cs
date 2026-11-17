using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Quick-report screenshots are the heaviest and most personal thing the app stores (they show balances). They are only
/// needed while a report is being looked at, so once a day at 02:10 UTC the pictures of reports older than 30 days are
/// removed. The text, technical details and the report itself stay.
/// </summary>
public class DiagnosticsRetentionWorker : BackgroundService
{
    public const int KeepScreenshotsDays = 30;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiagnosticsRetentionWorker> _logger;

    public DiagnosticsRetentionWorker(IServiceScopeFactory scopeFactory, ILogger<DiagnosticsRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(2).AddMinutes(10);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            await Task.Delay(nextRun - now, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-KeepScreenshotsDays);
                var cleared = await db.IssueDiagnostics
                    .Where(d => d.Screenshot != null && d.CreatedAt < cutoff)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Screenshot, (byte[]?)null)
                                              .SetProperty(d => d.ScreenshotContentType, (string?)null), stoppingToken);
                if (cleared > 0) _logger.LogInformation("Removed {Count} old report screenshots.", cleared);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Report screenshot clean-up failed.");
            }
        }
    }
}
