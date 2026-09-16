using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Services;

public class IssueRankingServiceTests
{
    private readonly IssueRankingService _rankingService = new();

    [Fact]
    public void CalculatePainScore_WithFinancialImpact_CalculatesHigherScoreThanWithout()
    {
        var issueWithMoney = new Issue
        {
            Title = "Payment Gateway Timeout",
            Description = "Loss of revenue",
            ImpactsMoney = true,
            Frequency = IssueFrequency.Always,
            Severity = IssueSeverity.Critical,
            TrustPenalty = 0,
            FinancialImpactAmount = null
        };

        var issueWithoutMoney = new Issue
        {
            Title = "UI Color Discrepancy",
            Description = "Purely cosmetic",
            ImpactsMoney = false,
            Frequency = IssueFrequency.Always,
            Severity = IssueSeverity.Critical,
            TrustPenalty = 0,
            FinancialImpactAmount = null
        };

        var scoreWithMoney = _rankingService.CalculatePainScore(issueWithMoney);
        var scoreWithoutMoney = _rankingService.CalculatePainScore(issueWithoutMoney);

        scoreWithMoney.Should().BeGreaterThan(scoreWithoutMoney);
    }

    [Fact]
    public void CalculatePainScore_IncludesFinancialImpactAmount_AndTrustPenalty()
    {
        var issue = new Issue
        {
            Title = "Duplicate Billing",
            Description = "Billed twice",
            ImpactsMoney = true,
            Frequency = IssueFrequency.Frequent, // 5
            Severity = IssueSeverity.Major,     // 3
            TrustPenalty = 50,
            FinancialImpactAmount = 2500m
        };

        // Base score = 100 * 5 * 3 = 1500
        // Trust penalty = 50
        // Financial impact = 2500
        // Total expected = 1500 + 50 + 2500 = 4050
        var score = _rankingService.CalculatePainScore(issue);

        score.Should().Be(4050);
    }

    [Theory]
    [InlineData(IssueSeverity.Critical, 5)]
    [InlineData(IssueSeverity.Major, 3)]
    [InlineData(IssueSeverity.Minor, 1)]
    public void CalculatePainScore_ScalesWithSeverity(IssueSeverity severity, int expectedMultiplier)
    {
        var issue = new Issue
        {
            Title = "Test Issue",
            Description = "Test Description",
            ImpactsMoney = false, // 10
            Frequency = IssueFrequency.Rare, // 1
            Severity = severity,
            TrustPenalty = 0,
            FinancialImpactAmount = null
        };

        var score = _rankingService.CalculatePainScore(issue);

        score.Should().Be(10 * 1 * expectedMultiplier);
    }
}
