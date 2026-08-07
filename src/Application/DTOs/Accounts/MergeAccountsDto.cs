namespace Application.DTOs.Accounts;

public class MergeAccountsDto
{
    public int SourceAccountId { get; set; }  // retired, its transactions move to target
    public int TargetAccountId { get; set; }  // survives
    // Deliberately explicit, not computed silently — see the service
    // method's comments for why. The frontend suggests a default
    // (typically the sum of both original balances) but this is always
    // what the user actually confirmed, not a guess baked into the backend.
    public decimal FinalBalance { get; set; }
}
