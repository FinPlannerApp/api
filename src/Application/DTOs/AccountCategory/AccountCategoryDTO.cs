using Domain.Enums;

namespace Application.DTOs.Categories;

public class AccountCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True if this category represents a liability (credit card, loan, EMI).
    /// Drives net worth exclusion in DashboardService — see DashboardService_NetWorthFix.cs.
    /// </summary>
    public bool IsLiability { get; set; }

    /// <summary>
    /// Determines which detail fields/UI apply to accounts in this category.
    /// </summary>
    public AccountType AccountType { get; set; }
}