namespace Application.DTOs.Accounts;

public class PaymentAppCashbackDto
{
    public string PaymentAppName { get; set; } = string.Empty;
    public decimal TotalPaid { get; set; }
    public decimal TotalCashback { get; set; }
    public decimal EffectiveRatePercent { get; set; }
    public int PaymentCount { get; set; }
}

public class CashbackInsightsDto
{
    public List<PaymentAppCashbackDto> ByApp { get; set; } = new();

    /// <summary>
    /// Sum of all cashback recorded as Indirect (wallet-based) — real
    /// money you've been told you'll receive, but that isn't reflected
    /// in any FinPlanner account balance because it landed somewhere
    /// FinPlanner can't see. A reminder total, not a tracked balance.
    /// </summary>
    public decimal UnclaimedWalletCashback { get; set; }
}
