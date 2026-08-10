namespace Application.DTOs.Accounts;

public class AdjustBalanceDto
{
    public int AccountId { get; set; }
    public decimal NewBalance { get; set; }
    public string? Reason { get; set; }
}
