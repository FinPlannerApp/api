using Application.Common.Models;

namespace Application.Contracts;

/// <summary>
/// Generates the 5 CSV reports promised in the synopsis (FR-08), never
/// actually built until now. Deliberately reuses the existing, already
/// timezone-fixed business logic (IDashboardService, GetBudgetProgressQuery,
/// ITransactionService) rather than recomputing anything independently —
/// the numbers in these reports need to match what the UI shows, not a
/// second, potentially-drifting implementation of the same math.
/// </summary>
public interface IReportService
{
    /// <summary>Income/expense/net summary + category breakdown for one month.</summary>
    Task<Result<byte[]>> GenerateMonthlySummaryReportAsync(string userId, int month, int year);

    /// <summary>Spending by category over an arbitrary date range, with % of total.</summary>
    Task<Result<byte[]>> GenerateCategoryAnalysisReportAsync(string userId, DateTime startDate, DateTime endDate);

    /// <summary>Every active budget vs. actual spend, as of a given date.</summary>
    Task<Result<byte[]>> GenerateBudgetVsActualReportAsync(string userId, DateTime asOfDate);

    /// <summary>Full transaction history for one account over a date range.</summary>
    Task<Result<byte[]>> GenerateAccountStatementReportAsync(string userId, int accountId, DateTime startDate, DateTime endDate);

    /// <summary>Every account, current balance, and asset/liability classification.</summary>
    Task<Result<byte[]>> GenerateNetWorthReportAsync(string userId);
}
