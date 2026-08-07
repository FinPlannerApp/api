using Domain.Rules;

namespace Application.Features.Insights.Rules;

/// <summary>
/// Compares current spending against a 4-6-months-ago baseline, not last
/// month — the engine's own built-in delta rule already catches one-month
/// blips, this is specifically for sustained, gradual creep that a
/// month-over-month comparison wouldn't flag until it's already large.
/// </summary>
public class LifestyleInflationRule : IFinancialRule
{
    public string RuleName => "Lifestyle Inflation";
    public string Description => "Flags a sustained increase in spending compared to 4-6 months ago.";

    public FinancialInsight Evaluate(RuleContext context)
    {
        if (context.BaselineExpenseFourToSixMonthsAgo <= 0)
            return new FinancialInsight { Triggered = false, Title = "", Message = "" };

        var increase = ((context.TotalExpense - context.BaselineExpenseFourToSixMonthsAgo) / context.BaselineExpenseFourToSixMonthsAgo) * 100;

        if (increase > 25)
        {
            return new FinancialInsight
            {
                Title = "Lifestyle creep 📈",
                Message = $"Your spending is {increase:F0}% higher than your average from 4-6 months ago — worth checking whether that's intentional.",
                Type = InsightType.Warning,
                Triggered = true
            };
        }

        return new FinancialInsight { Triggered = false, Title = "", Message = "" };
    }
}

/// <summary>
/// Compares spending in the 3 days right after the biggest income
/// transaction of the period (a reasonable proxy for "salary day" without
/// needing the user to explicitly configure what counts as salary)
/// against the average daily spend for the rest of the month.
/// </summary>
public class SalaryDaySpikeRule : IFinancialRule
{
    public string RuleName => "Salary-Day Spending Spike";
    public string Description => "Flags unusually high spending right after your largest income lands.";

    public FinancialInsight Evaluate(RuleContext context)
    {
        if (context.AverageDailySpendRestOfMonth <= 0)
            return new FinancialInsight { Triggered = false, Title = "", Message = "" };

        var threeDayAverage = context.SpendingInThreeDaysAfterLargestIncome / 3m;
        var ratio = threeDayAverage / context.AverageDailySpendRestOfMonth;

        if (ratio > 2.5m)
        {
            return new FinancialInsight
            {
                Title = "Salary-day spike 💸",
                Message = $"You spend about {ratio:F1}x your normal daily rate in the 3 days right after your biggest income lands this month.",
                Type = InsightType.Info,
                Triggered = true
            };
        }

        return new FinancialInsight { Triggered = false, Title = "", Message = "" };
    }
}

/// <summary>
/// A nudge, not a claim of actual waste — the app has no way to know if a
/// subscription is genuinely still being used. This just surfaces
/// long-running subscriptions as worth a periodic look, framed honestly
/// as a review prompt rather than an accusation.
/// </summary>
public class SubscriptionReviewNudgeRule : IFinancialRule
{
    public string RuleName => "Subscription Review Nudge";
    public string Description => "Nudges you to review subscriptions that have been running a long time.";

    public FinancialInsight Evaluate(RuleContext context)
    {
        if (context.OldestActiveSubscriptionMonths >= 6)
        {
            return new FinancialInsight
            {
                Title = "Time for a subscription check-in? 🔍",
                Message = $"Your longest-running active subscription has been going for {context.OldestActiveSubscriptionMonths} months. Worth a quick check that you're still using it.",
                Type = InsightType.Info,
                Triggered = true
            };
        }

        return new FinancialInsight { Triggered = false, Title = "", Message = "" };
    }
}
