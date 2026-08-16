namespace Application.DTOs.Accounts;

public class AccountPaymentSuggestionDto
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public bool HasSufficientBalance { get; set; }

    /// <summary>
    /// How much more this account would need, if HasSufficientBalance
    /// is false. Zero when the account already covers the amount.
    /// </summary>
    public decimal Shortfall { get; set; }
}
