using Application.Common.Models;
using Application.DTOs.Dashboard;

namespace Application.Contracts;

public interface IDashboardService
{
    Task<Result<PublicStatsDto>> GetPublicStatsAsync();
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null);
    Task<Result<List<SpendingByCategoryDto>>> GetSpendingByCategoryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null);
    Task<Result<AccountSummaryDto>> GetAccountSummaryAsync(string userId, int accountId, DateTime? startDate = null, DateTime? endDate = null);
    Task<Result<DashboardInsightsDto>> GetDeepInsightsAsync(string userId, DateTime? startDate = null, DateTime? endDate = null);
    Task<Result<List<MonthlyTrendPointDto>>> GetMonthlyTrendAsync(string userId, int monthsBack = 6);
}
