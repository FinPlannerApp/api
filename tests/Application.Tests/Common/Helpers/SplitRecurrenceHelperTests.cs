using Application.Common.Helpers;
using Domain.Entities.Split;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

public class SplitRecurrenceHelperTests
{
    [Theory]
    [InlineData(SplitRecurrenceFrequency.Daily, 3, "2026-03-04")]
    [InlineData(SplitRecurrenceFrequency.Weekly, 2, "2026-03-15")]
    [InlineData(SplitRecurrenceFrequency.Monthly, 1, "2026-04-01")]
    [InlineData(SplitRecurrenceFrequency.Monthly, 0, "2026-03-01")]
    public void OccurrenceDate_AdvancesByThePeriod(SplitRecurrenceFrequency f, int n, string expected)
        => SplitRecurrenceHelper.OccurrenceDate(new DateTime(2026, 3, 1), f, n).Should().Be(DateTime.Parse(expected));

    [Fact]
    public void Monthly_FromThe31st_ClampsToMonthEndAndThenReturnsToThe31st()
    {
        var start = new DateTime(2026, 1, 31);

        SplitRecurrenceHelper.OccurrenceDate(start, SplitRecurrenceFrequency.Monthly, 1).Should().Be(new DateTime(2026, 2, 28));
        SplitRecurrenceHelper.OccurrenceDate(start, SplitRecurrenceFrequency.Monthly, 2).Should().Be(new DateTime(2026, 3, 31));
        SplitRecurrenceHelper.OccurrenceDate(start, SplitRecurrenceFrequency.Monthly, 3).Should().Be(new DateTime(2026, 4, 30));
    }
}
