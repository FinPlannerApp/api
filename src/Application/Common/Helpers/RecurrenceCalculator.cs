using Domain.Enums;

namespace Application.Common.Helpers;

/// <summary>
/// Shared with RecurringTransactionJob's own next-date logic
/// conceptually, but kept as a separate copy here rather than a
/// cross-layer reference — Infrastructure depends on Application, not
/// the reverse, and this is a small enough calculation that
/// duplicating it is safer than restructuring project references for
/// one shared method.
/// </summary>
public static class RecurrenceCalculator
{
    public static DateTime CalculateNextOccurrenceOnOrAfter(
        DateTime startDate, RecurrenceFrequency frequency, RecurrenceDayOfWeek? customDays, DateTime referenceDate)
    {
        if (frequency == RecurrenceFrequency.Custom)
            return CalculateNextCustomOnOrAfter(startDate, customDays, referenceDate);

        var next = startDate;
        var safetyLimit = 10000;

        while (next < referenceDate && safetyLimit-- > 0)
        {
            next = frequency switch
            {
                RecurrenceFrequency.Daily => next.AddDays(1),
                RecurrenceFrequency.Weekly => next.AddDays(7),
                RecurrenceFrequency.Monthly => next.AddMonths(1),
                RecurrenceFrequency.Yearly => next.AddYears(1),
                _ => referenceDate // OneTime and any other non-recurring case
            };
        }

        return next;
    }

    private static DateTime CalculateNextCustomOnOrAfter(
        DateTime startDate, RecurrenceDayOfWeek? customDays, DateTime referenceDate)
    {
        if (customDays is null or RecurrenceDayOfWeek.None)
            return referenceDate; // degenerate input, nothing valid to compute

        var candidate = startDate < referenceDate ? referenceDate : startDate;

        for (int i = 0; i <= 7; i++)
        {
            var check = candidate.AddDays(i);
            if ((customDays.Value & DayOfWeekToFlag(check.DayOfWeek)) != 0)
                return check;
        }

        return referenceDate; // shouldn't be reachable if customDays has any bit set
    }

    private static RecurrenceDayOfWeek DayOfWeekToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => RecurrenceDayOfWeek.Monday,
        DayOfWeek.Tuesday => RecurrenceDayOfWeek.Tuesday,
        DayOfWeek.Wednesday => RecurrenceDayOfWeek.Wednesday,
        DayOfWeek.Thursday => RecurrenceDayOfWeek.Thursday,
        DayOfWeek.Friday => RecurrenceDayOfWeek.Friday,
        DayOfWeek.Saturday => RecurrenceDayOfWeek.Saturday,
        DayOfWeek.Sunday => RecurrenceDayOfWeek.Sunday,
        _ => RecurrenceDayOfWeek.None
    };
}

