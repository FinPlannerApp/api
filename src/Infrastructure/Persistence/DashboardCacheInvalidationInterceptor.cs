using System.Collections.Concurrent;
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
/// Includes anti-burst throttling to prevent redundant Redis sweeps on rapid consecutive saves.
/// </summary>
public sealed class DashboardCacheInvalidationInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> TrackedEntities = new(StringComparer.Ordinal)
    {
        "Transaction", "Account", "CreditCardDetails", "LoanDetails",
        "CreditCardPayment", "CreditCardBill", "RecurringTransaction",
        "Budget", "Subscription", "PaymentAppWalletLedgerEntry"
    };

    // One pending set per DbContext instance; the interceptor itself is a singleton.
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSweepTime = new(StringComparer.Ordinal);
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMilliseconds(1500);

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
        var now = DateTime.UtcNow;

        foreach (var userId in users)
        {
            if (_lastSweepTime.TryGetValue(userId, out var last) && (now - last) < ThrottleWindow)
            {
                // Throttled: Recent sweep already invalidated Redis for this user in this burst.
                continue;
            }
            _lastSweepTime[userId] = now;
            await _cache.RemoveByPrefixAsync($"dash::{userId}:");
        }
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

