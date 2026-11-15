namespace Application.DTOs.Transactions;

/// <summary>Expense total for one calendar day (spending heatmap).</summary>
public class DailySpendDto
{
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int Count { get; set; }
}
