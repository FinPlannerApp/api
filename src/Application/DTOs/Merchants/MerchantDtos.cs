namespace Application.DTOs.Merchants;

public class MerchantDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
}

public class UpsertMerchantDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = new();
}

public class MerchantSpendingDto
{
    public int MerchantId { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public decimal TotalSpent { get; set; }
    public int TransactionCount { get; set; }
}
