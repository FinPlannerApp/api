using System;
using Application.Common.Helpers;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

/// <summary>
/// RecurrenceCalculator is pure, dependency-free logic — no database,
/// no mocking needed — which makes it the most reliable place to start
/// real automated coverage. Every expected value below is either
/// computed by hand and shown in a comment, or derived programmatically
/// within the test itself (for weekday-dependent cases) rather than
/// hardcoded against an assumed calendar day-of-week, so these tests
/// stay correct regardless of when they're read or run.
/// </summary>
public class RecurrenceCalculatorTests
{
    [Fact]
    public void Daily_WhenStartDateIsBeforeReference_RollsForwardToExactReferenceDate()
    {
        // Daily from Jan 1 → Jan 2 → Jan 3 → Jan 4 → Jan 5. The loop
        // stops the moment `next` is no longer earlier than reference,
        // so landing exactly on Jan 5 is the correct, precise result —
        // not "the day after" or "the day before".
        var start = new DateTime(2026, 1, 1);
        var reference = new DateTime(2026, 1, 5);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Daily, null, reference);

        result.Should().Be(new DateTime(2026, 1, 5));
    }

    [Fact]
    public void Weekly_RollsForwardInSevenDaySteps()
    {
        // Jan 1 → Jan 8 → Jan 15 → Jan 22. Jan 22 is the first weekly
        // occurrence that is not earlier than Jan 20.
        var start = new DateTime(2026, 1, 1);
        var reference = new DateTime(2026, 1, 20);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Weekly, null, reference);

        result.Should().Be(new DateTime(2026, 1, 22));
    }

    [Fact]
    public void Monthly_RollsForwardByCalendarMonth()
    {
        // Jan 15 → Feb 15 → Mar 15 → Apr 15. Mar 15 is still earlier
        // than Mar 20, so the loop must advance one more step to Apr 15.
        var start = new DateTime(2026, 1, 15);
        var reference = new DateTime(2026, 3, 20);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Monthly, null, reference);

        result.Should().Be(new DateTime(2026, 4, 15));
    }

    [Fact]
    public void Monthly_FromMonthEndDate_ClampsCorrectlyThroughShorterMonths()
    {
        // Jan 31 → AddMonths(1) → Feb 28 (2026 is not a leap year, so
        // .NET clamps to the last valid day of February rather than
        // throwing or overflowing into March) → AddMonths(1) from Feb
        // 28 → Mar 28, not Mar 31. This is genuinely worth a named
        // test: the clamped day silently "shifts" permanently once it
        // passes through a short month, which is real, documented
        // .NET DateTime behavior worth confirming explicitly rather
        // than assuming.
        var start = new DateTime(2026, 1, 31);
        var reference = new DateTime(2026, 3, 1);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Monthly, null, reference);

        result.Should().Be(new DateTime(2026, 3, 28));
    }

    [Fact]
    public void Yearly_RollsForwardByCalendarYear()
    {
        var start = new DateTime(2024, 6, 1);
        var reference = new DateTime(2026, 1, 1);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Yearly, null, reference);

        // 2024-06-01 → 2025-06-01 → 2026-06-01, since 2025-06-01 is
        // still earlier than the 2026-01-01 reference.
        result.Should().Be(new DateTime(2026, 6, 1));
    }

    [Fact]
    public void WhenStartDateIsAlreadyOnOrAfterReference_ReturnsStartDateUnchanged()
    {
        // No rolling forward should happen at all — the loop condition
        // `next < referenceDate` is false on the very first check.
        var start = new DateTime(2026, 6, 1);
        var reference = new DateTime(2026, 1, 1);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Monthly, null, reference);

        result.Should().Be(start);
    }

    [Fact]
    public void Custom_WithNoDaysSet_ReturnsReferenceDate_DegenerateCase()
    {
        var start = new DateTime(2026, 1, 1);
        var reference = new DateTime(2026, 1, 10);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Custom, RecurrenceDayOfWeek.None, reference);

        result.Should().Be(reference);

        // Null should behave identically to None — both are "nothing
        // valid was actually configured".
        var resultFromNull = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Custom, null, reference);

        resultFromNull.Should().Be(reference);
    }

    [Fact]
    public void Custom_FindsTheNextMatchingWeekdayOnOrAfterReference()
    {
        // Computed relative to the reference date's own real
        // DayOfWeek, not a hardcoded assumption about what weekday a
        // specific calendar date falls on — this keeps the test
        // correct regardless of when it's read.
        var reference = new DateTime(2026, 1, 10);
        var start = reference.AddDays(-30); // safely in the past

        var targetFlag = DayOfWeekToFlagForTest(reference.DayOfWeek);

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            start, RecurrenceFrequency.Custom, targetFlag, reference);

        // The reference date's own weekday is a valid match, and the
        // search starts AT the reference date (via candidate = max(start,
        // reference)), so it should resolve to the reference date itself.
        result.Should().Be(reference);
        result.DayOfWeek.Should().Be(reference.DayOfWeek);
    }

    [Fact]
    public void Custom_WithMultipleDaysSet_FindsTheEarliestMatchingOne()
    {
        // Monday and Friday selected — starting the search from a
        // Wednesday reference should land on the very next Friday,
        // not skip past it to the following Monday.
        var wednesday = NextDateWithDayOfWeek(new DateTime(2026, 1, 1), DayOfWeek.Wednesday);
        var mask = RecurrenceDayOfWeek.Monday | RecurrenceDayOfWeek.Friday;

        var result = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
            startDate: wednesday.AddDays(-14),
            frequency: RecurrenceFrequency.Custom,
            customDays: mask,
            referenceDate: wednesday);

        result.DayOfWeek.Should().Be(DayOfWeek.Friday);
        (result - wednesday).Days.Should().BeInRange(0, 6);
    }

    // ---- test helpers, not production code ----

    private static RecurrenceDayOfWeek DayOfWeekToFlagForTest(DayOfWeek day) => day switch
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

    private static DateTime NextDateWithDayOfWeek(DateTime from, DayOfWeek target)
    {
        var d = from;
        while (d.DayOfWeek != target) d = d.AddDays(1);
        return d;
    }
}
