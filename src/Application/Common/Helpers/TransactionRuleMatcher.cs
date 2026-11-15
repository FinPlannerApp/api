using Domain.Entities;
using Domain.Enums;

namespace Application.Common.Helpers;

/// <summary>Pure matching logic for transaction rules (no database), so it can be unit tested.</summary>
public static class TransactionRuleMatcher
{
    public const int MaxRulesPerUser = 100;

    public static bool Matches(TransactionRule rule, string? description, decimal amount, TransactionType type)
    {
        if (!rule.IsActive || string.IsNullOrWhiteSpace(rule.MatchText)) return false;
        var text = (description ?? string.Empty).Trim();
        var needle = rule.MatchText.Trim();
        var ok = rule.MatchMode switch
        {
            1 => text.StartsWith(needle, StringComparison.OrdinalIgnoreCase),
            2 => string.Equals(text, needle, StringComparison.OrdinalIgnoreCase),
            _ => text.Contains(needle, StringComparison.OrdinalIgnoreCase)
        };
        if (!ok) return false;
        if (rule.TypeFilter.HasValue && rule.TypeFilter.Value != type) return false;
        if (rule.MinAmount.HasValue && amount < rule.MinAmount.Value) return false;
        if (rule.MaxAmount.HasValue && amount > rule.MaxAmount.Value) return false;
        return true;
    }

    /// <summary>The first matching rule (lowest Priority, then oldest), or null.</summary>
    public static TransactionRule? FirstMatch(IEnumerable<TransactionRule> rules, string? description, decimal amount, TransactionType type) =>
        rules.OrderBy(r => r.Priority).ThenBy(r => r.Id).FirstOrDefault(r => Matches(r, description, amount, type));
}
