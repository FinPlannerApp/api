using Domain.Enums;

namespace Application.DTOs.Transactions;

/// <summary>A recently deleted transaction that can still be restored, with what restoring it would change.</summary>
public class UndoableTransactionDto
{
    public int TransactionId { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public DateTime Date { get; set; }
    public string? CategoryName { get; set; }
    public DateTime DeletedAt { get; set; }
    /// <summary>Seconds left from the server's clock — the UI counts down from this, never from its own.</summary>
    public int SecondsLeft { get; set; }

    public bool CanUndo { get; set; } = true;
    /// <summary>Why it can't be restored right now (e.g. it would overdraw the account).</summary>
    public string? BlockedReason { get; set; }

    public decimal BalanceNow { get; set; }
    public decimal BalanceAfter { get; set; }
    public bool IsLiability { get; set; }

    /// <summary>Budgets this restore would move (the expense's category budget), null when none apply.</summary>
    public UndoBudgetImpactDto? Budget { get; set; }
}

public class UndoBudgetImpactDto
{
    public string Name { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal SpentNow { get; set; }
    public decimal SpentAfter { get; set; }
    public int AlertThresholdPercent { get; set; }
}
