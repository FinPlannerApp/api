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

        // Votes are left out of the PainScore formula per original design —
        // "Upvotes are lazy. You need weighted pain."
        // They're tracked separately and added as a tie-breaker in sorting.
        
        return baseScore;
    }
}
