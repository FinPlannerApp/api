namespace Domain.Entities;

/// <summary>
/// User-defined, same philosophy as everything else in this app —
/// nothing here is auto-extracted or guessed from transaction text. A
/// Merchant is created explicitly, aliases are added explicitly, and
/// tagging a transaction with a merchant is always a suggestion the user
/// confirms, never automatic.
/// </summary>
public class Merchant : BaseEntity
{
    public required string UserId { get; set; }
    public required string Name { get; set; }
    public ICollection<MerchantAlias> Aliases { get; set; } = new List<MerchantAlias>();
}

/// <summary>
/// Separate child table rather than a JSON/delimited list on Merchant
/// itself — matches how every other one-to-many relationship in this app
/// is modeled (CreditCardDetails, etc.), not a new pattern introduced
/// just for this.
/// </summary>
public class MerchantAlias : BaseEntity
{
    public int MerchantId { get; set; }
    public Merchant Merchant { get; set; } = null!;
    public required string Alias { get; set; }
}
