using System;
using Application.Common.Helpers;
using Application.DTOs.Budgets;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

public class MonthStartDayTests
{
    // App time zone is Asia/Kolkata (UTC+5:30): local midnight == 18:30 UTC the previous day.
    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void StartDay1_IsTheCalendarMonth()
    {
        var instant = Utc(2026, 3, 12, 6);
        AppTimeZone.MonthBoundsUtc(instant, 1).Should().Be(AppTimeZone.MonthBoundsUtc(instant));
    }

    [Fact]
    public void AfterTheStartDay_TheMonthRunsFromThisMonthToTheDayBeforeNextStart()
    {
        var (start, end) = AppTimeZone.MonthBoundsUtc(Utc(2026, 3, 12, 6), 5);
        start.Should().Be(Utc(2026, 3, 4, 18, 30));              // 5 Mar 00:00 IST
        end.Should().Be(Utc(2026, 4, 4, 18, 30).AddTicks(-1));   // 4 Apr 23:59:59.9999999 IST
    }

    [Fact]
    public void BeforeTheStartDay_TheMonthStartedLastMonth()
    {
        var (start, end) = AppTimeZone.MonthBoundsUtc(Utc(2026, 3, 2, 6), 5);
        start.Should().Be(Utc(2026, 2, 4, 18, 30));              // 5 Feb
        end.Should().Be(Utc(2026, 3, 4, 18, 30).AddTicks(-1));   // 4 Mar
    }

    [Fact]
    public void StartDay_IsClampedTo28_SoShortMonthsNeverBreak()
    {
        var (start, _) = AppTimeZone.MonthBoundsUtc(Utc(2026, 3, 30, 6), 99);
        start.Should().Be(Utc(2026, 3, 27, 18, 30));             // 28 Mar 00:00 IST
    }

    [Theory]
    [InlineData(40, 80, "safe")]
    [InlineData(80, 80, "alert")]
    [InlineData(65, 60, "alert")]
    [InlineData(99, 100, "safe")]   // threshold 100 → only the limit alerts
    [InlineData(100, 100, "limit")]
    [InlineData(130, 80, "over")]
    public void Status_FollowsTheAlertLevel(int percent, int threshold, string expected)
    {
        var p = new BudgetProgressDto { BudgetAmount = 100, SpentAmount = percent, AlertThresholdPercent = threshold };
        p.Status.Should().Be(expected);
    }

    [Fact]
    public void Pace_IsProjectedFromDaysElapsed()
    {
        var p = new BudgetProgressDto { BudgetAmount = 3000, SpentAmount = 1500, DaysElapsed = 10, DaysTotal = 30 };
        p.ExpectedPercent.Should().Be(33.3m);
        p.ProjectedSpend.Should().Be(4500m);
    }
}
