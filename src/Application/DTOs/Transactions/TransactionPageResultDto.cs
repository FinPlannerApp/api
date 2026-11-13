using Application.Common.Models;

namespace Application.DTOs.Transactions;

public class TransactionPageResultDto
{
    public PaginatedResult<TransactionDto> Transactions { get; set; } = null!;
    /// <summary>Spendable cash (bank/cash/marked-spendable accounts) at the start of the selected month.</summary>
    public decimal BalanceBroughtForward { get; set; }
    /// <summary>Spendable cash at the end of the selected month (actual balance, not B/F + income - expense).</summary>
    public decimal ClosingBalance { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalSavings { get; set; }
    public List<string> AvailableCategories { get; set; } = new();
}
