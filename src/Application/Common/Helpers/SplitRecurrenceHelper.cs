using Domain.Entities.Split;

namespace Application.Common.Helpers;

public static class SplitRecurrenceHelper
{
    /// <summary>
    /// The date of the n-th occurrence (0 = the start date). Always counted from the start, so a
    /// monthly expense that begins on the 31st lands on the last day of shorter months and then
    /// goes back to the 31st, instead of drifting to the 28th for good.
    /// </summary>
    public static DateTime OccurrenceDate(DateTime start, SplitRecurrenceFrequency frequency, int n) => frequency switch
    {
        SplitRecurrenceFrequency.Daily => start.AddDays(n),
        SplitRecurrenceFrequency.Weekly => start.AddDays(7 * n),
        SplitRecurrenceFrequency.Monthly => start.AddMonths(n),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };
}
