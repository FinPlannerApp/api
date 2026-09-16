using System;
using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

/// <summary>
/// RecurringDateHelper.GetMostRecentOccurrence is pure, dependency-free
/// logic — verified directly against the real implementation in
/// api-main/src/Application/Common/Helpers/RecurringDateHelper.cs.
/// It answers a different question from RecurrenceCalculator (which
/// finds the NEXT future occurrence): this finds the most recent PAST
/// occurrence of a stored "day of month N" as of a given date, used
/// for statement-cycle boundary calculations (Section 4.6.1-adjacent).
/// </summary>
public class RecurringDateHelperTests
{
    [Fact]
    public void WhenStoredDayHasAlreadyOccurredThisMonth_ReturnsThisMonthsOccurrence()
    {
        // Stored day is the 5th; "as of" is the 20th of the same month —
        // this month's 5th has already passed, so it should be returned
        // directly, not last month's.
        var storedDate = new DateTime(2026, 1, 5);
        var asOf = new DateTime(2026, 3, 20);

        var result = RecurringDateHelper.GetMostRecentOccurrence(storedDate, asOf);

        result.Date.Should().Be(new DateTime(2026, 3, 5));
    }

    [Fact]
    public void WhenStoredDayHasNotYetOccurredThisMonth_ReturnsLastMonthsOccurrence()
    {
        // Stored day is the 25th; "as of" is the 10th of the same month —
        // this month's 25th is still in the future, so the most recent
        // actual occurrence was last month's.
        var storedDate = new DateTime(2026, 1, 25);
        var asOf = new DateTime(2026, 3, 10);

        var result = RecurringDateHelper.GetMostRecentOccurrence(storedDate, asOf);

        result.Date.Should().Be(new DateTime(2026, 2, 25));
    }

    [Fact]
    public void StoredDay31_InAMonthWithFewerDays_ClampsRatherThanErrors()
    {
        // Stored day is the 31st; "as of" is February 10th, 2026 (28
        // days). February has no 31st — this must clamp to the 28th,
        // not throw and not roll into March.
        var storedDate = new DateTime(2026, 1, 31);
        var asOf = new DateTime(2026, 2, 10);

        var result = RecurringDateHelper.GetMostRecentOccurrence(storedDate, asOf);

        // Feb 28 (2026 is not a leap year) is still after Feb 10, so the
        // most recent occurrence is January's, also clamped: Jan 31 is
        // valid, so this resolves to Jan 31 itself.
        result.Date.Should().Be(new DateTime(2026, 1, 31));
    }

    [Fact]
    public void StoredDay31_AsOfLateInAShortMonth_ClampsToThatMonthsLastDay()
    {
        // "As of" Feb 28th itself — this month's clamped occurrence
        // (Feb 28) has already happened by definition (<=), so it
        // should be returned rather than falling back to January.
        var storedDate = new DateTime(2026, 1, 31);
        var asOf = new DateTime(2026, 2, 28);

        var result = RecurringDateHelper.GetMostRecentOccurrence(storedDate, asOf);

        result.Date.Should().Be(new DateTime(2026, 2, 28));
    }

    [Fact]
    public void WhenAsOfDateExactlyMatchesTheStoredDay_ReturnsThatSameDate()
    {
        var storedDate = new DateTime(2026, 1, 15);
        var asOf = new DateTime(2026, 4, 15);

        var result = RecurringDateHelper.GetMostRecentOccurrence(storedDate, asOf);

        result.Date.Should().Be(new DateTime(2026, 4, 15));
    }
}
