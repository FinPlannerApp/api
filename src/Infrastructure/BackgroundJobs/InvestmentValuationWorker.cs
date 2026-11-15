using Application.Contracts;
using Application.DTOs.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>Every 6 hours: refresh automatic investment values, and record estimated interest for accounts that opted in.</summary>
public class InvestmentValuationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<InvestmentValuationWorker> _logger;
    public InvestmentValuationWorker(IServiceScopeFactory scopes, ILogger<InvestmentValuationWorker> logger)
    { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // small start-up delay so the app is fully up first
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var prices = scope.ServiceProvider.GetRequiredService<IMarketPriceService>();
                var r = await prices.RefreshAsync(null, null, stoppingToken);
                _logger.LogInformation("Investment valuation: {Message}", r.Message);
            }
            catch (Exception ex) { _logger.LogError(ex, "Investment valuation run failed."); }

            try { await PostAutoInterestAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Automatic interest posting failed."); }

            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task PostAutoInterestAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var users = await ctx.BankAccountDetails.AsNoTracking()
            .Where(b => b.AutoPostInterest && !b.Account.IsDeleted && !b.Account.IsArchived)
            .Select(b => b.Account.UserId).Distinct().ToListAsync(ct);

        foreach (var userId in users)
        {
            using var userScope = _scopes.CreateScope();
            var accounts = userScope.ServiceProvider.GetRequiredService<IAccountService>();
            var overview = await accounts.GetInterestOverviewAsync(userId);
            if (overview.IsFailure) continue;
            // Only recent periods: turning auto-post on must never back-fill months of old estimates.
            var cutoff = DateTime.UtcNow.Date.AddDays(-45);
            foreach (var due in overview.Value.Due.Where(d => d.AutoPost && d.PeriodEnd >= cutoff))
            {
                var res = await accounts.PostInterestAsync(userId, new PostInterestDto
                { AccountId = due.AccountId, PeriodEnd = due.PeriodEnd, Amount = due.EstimatedAmount, IsAuto = true });
                if (res.IsFailure) _logger.LogInformation("Auto interest skipped for account {Id}: {Err}", due.AccountId, res.Error.Description);
            }
        }
    }
}
