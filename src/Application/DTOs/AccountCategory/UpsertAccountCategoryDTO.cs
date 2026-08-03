namespace Application.DTOs.AccountCategory;

public class UpsertAccountCategoryDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// User-set flag: is this category a liability (credit card, loan, EMI)?
    /// Defaults to false if the frontend doesn't send it yet (e.g. before you
    /// wire up the toggle in the category edit form).
    /// </summary>
    public bool IsLiability { get; set; } = false;
}