namespace Application.DTOs.TransactionCategory;

public class UpsertTransactionCategoryDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }
    public bool IsTransferCategory { get; set; } = false;
    public int? ParentCategoryId { get; set; }
}