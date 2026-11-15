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

        // Monthly / yearly: always count from the ORIGINAL start date (start + k months). Stepping
        // "previous + 1 month" would drift permanently: Jan 31 -> Feb 28 -> Mar 28 -> ...
        if (frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly or RecurrenceFrequency.Quarterly or RecurrenceFrequency.HalfYearly)
        {
            var step = MonthsPerPeriod(frequency);
            for (var k = 0; k < 5000 && next < referenceDate; k++)
                next = startDate.AddMonths(k * step);
            return next;
        }

        var safetyLimit = 10000;
        while (next < referenceDate && safetyLimit-- > 0)
        {
            next = frequency switch
            {
                RecurrenceFrequency.Daily => next.AddDays(1),
                RecurrenceFrequency.Weekly => next.AddDays(7),
                _ => referenceDate // OneTime and any other non-recurring case
            };
        }

        return next;
    }

    /// <summary>
    /// The occurrence after <paramref name="currentUtc"/> for a monthly/yearly schedule that began at
    /// <paramref name="startUtc"/>. Counted from the start date on the IST calendar, so the day of month
    /// (31st, 1st ...) never drifts and a bill on the 1st never slides to the 29th.
    /// </summary>
    public static int MonthsPerPeriod(RecurrenceFrequency f) => f switch
    {
        RecurrenceFrequency.Quarterly => 3,
        RecurrenceFrequency.HalfYearly => 6,
        RecurrenceFrequency.Yearly => 12,
        _ => 1,
    };

    /// <summary>Same as <see cref="NextMonthlyOrYearlyAfter"/> for any whole-month step (quarterly, half-yearly...).</summary>
    public static DateTime NextEveryMonthsAfter(DateTime startUtc, DateTime currentUtc, int months)
    {
        var start = startUtc.ToLocal();
        var cur = currentUtc.ToLocal();
        for (var k = 1; k < 5000; k++)
        {
            var candidate = start.AddMonths(k * months);
            if (candidate > cur) return candidate.ToUtc();
        }
        return cur.AddMonths(months).ToUtc();
    }

    public static DateTime NextMonthlyOrYearlyAfter(DateTime startUtc, DateTime currentUtc, bool yearly)
    {
        var start = startUtc.ToLocal();
        var cur = currentUtc.ToLocal();
        for (var k = 1; k < 5000; k++)
        {
            var candidate = yearly ? start.AddYears(k) : start.AddMonths(k);
            if (candidate > cur) return candidate.ToUtc();
        }
        return (yearly ? cur.AddYears(1) : cur.AddMonths(1)).ToUtc();
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

