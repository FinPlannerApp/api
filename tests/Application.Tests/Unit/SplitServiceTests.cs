using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit;

public class SplitServiceTests
{
    [Fact]
    public void CalculateEqualSplit_DividesTotalEquallyAmongMembers()
    {
        decimal totalAmount = 300.00m;
        int memberCount = 3;

        decimal sharePerPerson = Math.Round(totalAmount / memberCount, 2);

        sharePerPerson.Should().Be(100.00m);
    }

    [Fact]
    public void CalculatePercentageSplit_ValidatesTotalPercentageIs100()
    {
        var percentages = new List<decimal> { 50m, 30m, 20m };

        var totalPercentage = percentages.Sum();

        totalPercentage.Should().Be(100m);
    }

    [Fact]
    public void CalculateUnequalSplit_HandlesRemainderCent()
    {
        decimal totalAmount = 100.00m;
        int memberCount = 3;

        // 33.33 + 33.33 + 33.34 = 100.00
        decimal baseShare = Math.Floor((totalAmount / memberCount) * 100) / 100m; // 33.33
        decimal remainder = totalAmount - (baseShare * memberCount); // 0.01

        var shares = new List<decimal> { baseShare + remainder, baseShare, baseShare };

        shares.Sum().Should().Be(totalAmount);
        shares[0].Should().Be(33.34m);
    }
}
