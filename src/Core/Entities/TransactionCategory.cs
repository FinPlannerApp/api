namespace Domain.Entities;

public class TransactionCategory : BaseEntity
{
    public required string Name { get; set; }
    public required string UserId { get; set; }
    public bool IsTransferCategory { get; set; } = false;

    /// <summary>
    /// Null for top-level categories. Set to a parent's Id for a
    /// subcategory. Deliberately ONE level deep only — a subcategory
    /// can't itself have children. Enforced in the service layer, not
    /// by the schema (the schema technically allows deeper nesting;
    /// the validation is what keeps it to one level). One level covers
    /// the actual use case — "Food → Groceries / Dining Out" — without
    /// the rollup logic having to handle arbitrary-depth recursion,
    /// which would make every one of the seven aggregation sites
    /// meaningfully more complex for no described benefit.
    /// </summary>
    public int? ParentCategoryId { get; set; }
    public TransactionCategory? ParentCategory { get; set; }
    public ICollection<TransactionCategory> SubCategories { get; set; } = new List<TransactionCategory>();
}