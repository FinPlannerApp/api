namespace Application.Contracts;

/// <summary>
/// The one place that knows how dashboard cache keys are shaped. Everything cached for a user's
/// dashboard lives under <see cref="UserPrefix"/>, so a single prefix removal invalidates it all
/// (a cheap version bump in the cache service).
/// </summary>
public static class DashboardCacheKeys
{
    public static string UserPrefix(string userId) => $"dash::{userId}:";
    public static string Summary(string userId, DateTime start, DateTime end) => $"{UserPrefix(userId)}sum:{start:yyyyMMdd}:{end:yyyyMMdd}";
    public static string Categories(string userId, DateTime start, DateTime end) => $"{UserPrefix(userId)}cat:{start:yyyyMMdd}:{end:yyyyMMdd}";
    public static string Insights(string userId, DateTime start, DateTime end) => $"{UserPrefix(userId)}ins:{start:yyyyMMdd}:{end:yyyyMMdd}";

    /// <summary>Drop every cached dashboard figure for this user. Call after any money-affecting write.</summary>
    public static Task InvalidateDashboardAsync(this ICacheService cache, string userId)
        => cache.RemoveByPrefixAsync(UserPrefix(userId));
}
