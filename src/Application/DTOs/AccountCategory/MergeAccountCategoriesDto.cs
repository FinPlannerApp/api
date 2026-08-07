namespace Application.DTOs.AccountCategory;

public class MergeAccountCategoriesDto
{
    public int SourceCategoryId { get; set; }  // gets retired
    public int TargetCategoryId { get; set; }  // survives, absorbs source's accounts
}
