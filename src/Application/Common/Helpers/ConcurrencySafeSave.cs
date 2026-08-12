using Application.Common.Models;
using Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

public static class ConcurrencySafeSave
{
    /// <summary>
    /// Wraps SaveChangesAsync and translates a concurrency conflict into
    /// a normal Result failure instead of letting DbUpdateConcurrencyException
    /// propagate as an unhandled 500. The message is written for an end
    /// user, not a developer — "refresh and try again" is genuinely the
    /// correct action here, not a symptom to investigate.
    /// </summary>
    public static async Task<Result<bool>> TrySaveChangesAsync(IApplicationDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return Result.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<bool>(new Error(
                "Concurrency.Conflict",
                "This account was just updated by another request — please refresh and try again."));
        }
    }
}
