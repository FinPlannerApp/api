namespace Application.Common.Helpers;

public static class RecurringDateHelper
{
    /// <summary>
    /// Given a stored date representing "day of month N," returns the
    /// most recent occurrence of that day at or before asOfUtc. Handles
    /// months with fewer days than N correctly (e.g. day 31 in February
    /// becomes the 28th/29th, not an error or a rollover into March).
    /// </summary>
    public static DateTime GetMostRecentOccurrence(DateTime storedDate, DateTime asOfUtc)
    {
        var dayOfMonth = storedDate.Day;
        var asOfLocal = asOfUtc.ToLocal().Date;

        var thisMonthDay = Math.Min(dayOfMonth, DateTime.DaysInMonth(asOfLocal.Year, asOfLocal.Month));
        var thisMonthDate = new DateTime(asOfLocal.Year, asOfLocal.Month, thisMonthDay);

        if (thisMonthDate <= asOfLocal)
            return DateTime.SpecifyKind(thisMonthDate, DateTimeKind.Unspecified).ToUtc();

        var lastMonth = asOfLocal.AddMonths(-1);
        var lastMonthDay = Math.Min(dayOfMonth, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
        var lastMonthDate = new DateTime(lastMonth.Year, lastMonth.Month, lastMonthDay);

        return DateTime.SpecifyKind(lastMonthDate, DateTimeKind.Unspecified).ToUtc();
    }
}
