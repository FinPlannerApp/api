using Domain.Entities;

namespace Application.Services;

public class IssueRankingService
{
    // Weights
    private const int ImpactMoneyWeight = 100;
    private const int ImpactNoMoneyWeight = 10;
    
    private const int FreqAlwaysWeight = 10;
    private const int FreqFrequentWeight = 5;
    private const int FreqRareWeight = 1;
    
    private const int SevCriticalWeight = 5;
    private const int SevMajorWeight = 3;
    private const int SevMinorWeight = 1;

    public double CalculatePainScore(Issue issue)
    {
        double impactScore = issue.ImpactsMoney ? ImpactMoneyWeight : ImpactNoMoneyWeight;
        
        double freqScore = issue.Frequency switch
        {
            IssueFrequency.Always => FreqAlwaysWeight,
            IssueFrequency.Frequent => FreqFrequentWeight,
            IssueFrequency.Sometimes => 3,
            _ => FreqRareWeight
        };

        double sevScore = issue.Severity switch
        {
            IssueSeverity.Critical => SevCriticalWeight,
            IssueSeverity.Major => SevMajorWeight,
            _ => SevMinorWeight
        };

        double baseScore = (impactScore * freqScore * sevScore);
        
        // Add TrustPenalty (if users are angry/churning)
        baseScore += issue.TrustPenalty;
        
        // Add Financial Risk directly to score
        // Log scale could prevent millions from breaking the score, but
        // the original formula implies simple addition: Final Score = ... + FinancialRisk
        if (issue.FinancialImpactAmount.HasValue && issue.FinancialImpactAmount.Value > 0)
        {
            baseScore += (double)issue.FinancialImpactAmount.Value;
        }

        // Each net vote is worth 10 points. VoteService applies the same +/-10 when someone votes, so
        // recomputing the score after an edit (UpdateIssue) keeps the votes instead of wiping them (SEC-32).
        baseScore += issue.Votes * 10;

        return baseScore;
    }
}
