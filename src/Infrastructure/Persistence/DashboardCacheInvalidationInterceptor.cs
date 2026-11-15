using System.Runtime.CompilerServices;
using Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence;

/// <summary>
/// Clears a user's cached dashboard numbers the moment ANY save touches money-related
/// data. Previously each service method had to remember to invalidate the cache, and
/// several paths (jobs, merges, loan/card flows) did not — so the dashboard kept showing
/// old numbers for up to the cache TTL. Doing it at SaveChanges makes it impossible to forget.
/// Invalidation is a plain in-memory version bump (no Redis command by default), so bursts of
/// saves cost nothing and there is deliberately NO throttle: skipping a bump could show old numbers.
/// </summary>
public sealed class DashboardCacheInvalidationInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> TrackedEntities = new(StringComparer.Ordinal)
    {
        "Transaction", "Account", "CreditCardDetails", "LoanDetails", "InvestmentDetails",
        "CreditCardPayment", "CreditCardBill", "RecurringTransaction",
        "Budget", "Subscription", "PaymentAppWalletLedgerEntry",
        "AccountCategory", "TransactionCategory", "Goal", "SavingsBucket"
    };

    // One pending set per DbContext instance; the interceptor itself is a singleton.
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();
    private readonly ICacheService _cache;

    public DashboardCacheInvalidationInterceptor(ICacheService cache) => _cache = cache;

    private void Collect(DbContext? context)
    {
        if (context is null) return;

        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (!TrackedEntities.Contains(entry.Metadata.ClrType.Name))
                continue;

            var userId = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == "UserId")?.CurrentValue as string;

            // CreditCardDetails / LoanDetails carry no UserId of their own: use their account's owner.
            if (string.IsNullOrEmpty(userId))
            {
                var accountIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "AccountId");
                if (accountIdProp?.CurrentValue is int accountId)
                {
                    userId = context.ChangeTracker.Entries<Domain.Entities.Account>()
                        .FirstOrDefault(a => a.Entity.Id == accountId)?.Entity.UserId;
                }
            }

            if (!string.IsNullOrEmpty(userId))
                users.Add(userId);
        }

        _pending.Remove(context);
        if (users.Count > 0)
            _pending.Add(context, users);
    }

    private async Task FlushAsync(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var users))
            return;

        _pending.Remove(context);
        foreach (var userId in users)
            await _cache.InvalidateDashboardAsync(userId);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushAsync(eventData.Context).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}

