using System.Globalization;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using Application.Features.Budgets.Queries;
using CsvHelper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Application; // QueryParameters lives in the bare Application namespace
namespace Application.Services;

public class ReportService : IReportService
{
    private readonly IApplicationDbContext _context;
    private readonly IDashboardService _dashboardService;
    private readonly ITransactionService _transactionService;
    private readonly ISender _sender;

    public ReportService(
        IApplicationDbContext context,
        IDashboardService dashboardService,
        ITransactionService transactionService,
        ISender sender)
    {
        _context = context;
        _dashboardService = dashboardService;
        _transactionService = transactionService;
        _sender = sender;
    }

    // ── 1. Monthly Summary ──────────────────────────────────────────────────
    public async Task<Result<byte[]>> GenerateMonthlySummaryReportAsync(string userId, int month, int year)
    {
        // Reuse AppTimeZone the same way the dashboard endpoints do, so a
        // report for "August 2026" covers exactly the same local-calendar
        // window the UI would show for that month — not a UTC-shifted one.
        var localMonthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localMonthEnd = localMonthStart.AddMonths(1).AddTicks(-1);
        var utcStart = localMonthStart.ToUtc();
        var utcEnd = localMonthEnd.ToUtc();

        var summaryResult = await _dashboardService.GetSummaryAsync(userId, utcStart, utcEnd);
        if (!summaryResult.IsSuccess)
            return Result.Failure<byte[]>(summaryResult.Error);

        var categoryResult = await _dashboardService.GetSpendingByCategoryAsync(userId, utcStart, utcEnd);
        if (!categoryResult.IsSuccess)
            return Result.Failure<byte[]>(categoryResult.Error);

        var summary = summaryResult.Value!;
        var categories = categoryResult.Value!;

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField($"Monthly Summary — {localMonthStart:MMMM yyyy}");
            csv.NextRecord();
            csv.NextRecord();

            csv.WriteField("Metric");
            csv.WriteField("Amount");
            csv.NextRecord();

            csv.WriteField("Brought Forward");
            csv.WriteField(summary.BroughtForwardAmount.ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Monthly Income");
            csv.WriteField(summary.MonthlyIncome.ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Monthly Expenses");
            csv.WriteField(summary.MonthlyExpenses.ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Net Savings This Month");
            csv.WriteField((summary.MonthlyIncome - summary.MonthlyExpenses).ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Current Net Worth");
            csv.WriteField(summary.NetWorth.ToString("F2"));
            csv.NextRecord();
            csv.NextRecord();

            csv.WriteField("Category");
            csv.WriteField("Amount");
            csv.NextRecord();

            foreach (var cat in categories.OrderByDescending(c => c.TotalAmount))
            {
                csv.WriteField(cat.CategoryName);
                csv.WriteField(cat.TotalAmount.ToString("F2"));
                csv.NextRecord();
            }
        }

        return Result.Success(memoryStream.ToArray());
    }

    // ── 2. Category Analysis ────────────────────────────────────────────────
    public async Task<Result<byte[]>> GenerateCategoryAnalysisReportAsync(string userId, DateTime startDate, DateTime endDate)
    {
        var categoryResult = await _dashboardService.GetSpendingByCategoryAsync(userId, startDate, endDate);
        if (!categoryResult.IsSuccess)
            return Result.Failure<byte[]>(categoryResult.Error);

        var categories = categoryResult.Value!;
        var total = categories.Sum(c => c.TotalAmount);

        var rows = categories
            .OrderByDescending(c => c.TotalAmount)
            .Select(c => new CategoryAnalysisRow
            {
                Category = c.CategoryName,
                Amount = c.TotalAmount,
                PercentOfTotal = total > 0 ? Math.Round(c.TotalAmount / total * 100, 1) : 0
            });

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }

        return Result.Success(memoryStream.ToArray());
    }

    // ── 3. Budget vs Actual ─────────────────────────────────────────────────
    public async Task<Result<byte[]>> GenerateBudgetVsActualReportAsync(string userId, DateTime asOfDate)
    {
        var budgetResult = await _sender.Send(new GetBudgetProgressQuery(userId, asOfDate));
        if (!budgetResult.IsSuccess)
            return Result.Failure<byte[]>(budgetResult.Error);

        var rows = budgetResult.Value!.Select(b => new BudgetVsActualRow
        {
            Category = b.CategoryName ?? "All Categories",
            Period = b.Period.ToString(),
            BudgetAmount = b.BudgetAmount,
            SpentAmount = b.SpentAmount,
            RemainingAmount = b.RemainingAmount,
            PercentageUsed = Math.Round(b.PercentageUsed, 1),
            DaysRemainingInPeriod = b.DaysRemainingInPeriod,
            DailyAllowance = Math.Round(b.DailyAllowance, 2)
        });

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }

        return Result.Success(memoryStream.ToArray());
    }

    // ── 4. Account Statement ────────────────────────────────────────────────
    public async Task<Result<byte[]>> GenerateAccountStatementReportAsync(string userId, int accountId, DateTime startDate, DateTime endDate)
    {
        var queryParams = new QueryParameters
        {
            PageNumber = 1,
            PageSize = 1000, // report cap — see README for why, and what to do if a real account exceeds this
            SortBy = "date",
            SortOrder = "asc",
            Filters = new Dictionary<string, string>()
        };

        var txResult = await _transactionService.GetTransactionsAsync(userId, accountId, queryParams);
        if (!txResult.IsSuccess)
            return Result.Failure<byte[]>(txResult.Error);

        var rows = txResult.Value!.Data
            .Where(t => t.Date >= startDate && t.Date <= endDate)
            .OrderBy(t => t.Date)
            .Select(t => new AccountStatementRow
            {
                Date = t.Date.ToLocal().ToString("yyyy-MM-dd"),
                Description = t.Description,
                Category = t.CategoryName ?? "Uncategorized",
                Type = t.Type.ToString(),
                Amount = t.Amount
            });

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }

        return Result.Success(memoryStream.ToArray());
    }

    // ── 5. Net Worth ────────────────────────────────────────────────────────
    public async Task<Result<byte[]>> GenerateNetWorthReportAsync(string userId)
    {
        var accounts = await _context.Accounts
            .AsNoTracking()
            .Include(a => a.AccountCategory)
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .Select(a => new NetWorthRow
            {
                AccountName = a.Name,
                Category = a.AccountCategory.Name,
                Classification = a.AccountCategory.IsLiability ? "Liability" : "Asset",
                Balance = a.Balance
            })
            .ToListAsync();

        var totalAssets = accounts.Where(a => a.Classification == "Asset").Sum(a => a.Balance);
        var totalLiabilitiesRaw = accounts.Where(a => a.Classification == "Liability").Sum(a => a.Balance);
        var netWorth = totalAssets + totalLiabilitiesRaw;

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(accounts.OrderByDescending(a => a.Balance));
            csv.NextRecord();

            csv.WriteField("Total Assets");
            csv.WriteField(totalAssets.ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Total Liabilities");
            csv.WriteField(Math.Abs(totalLiabilitiesRaw).ToString("F2"));
            csv.NextRecord();

            csv.WriteField("Net Worth");
            csv.WriteField(netWorth.ToString("F2"));
            csv.NextRecord();
        }

        return Result.Success(memoryStream.ToArray());
    }
}

// ── Row shapes for CsvHelper.WriteRecords<T> — property names become headers ──

internal class CategoryAnalysisRow
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PercentOfTotal { get; set; }
}

internal class BudgetVsActualRow
{
    public string Category { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal PercentageUsed { get; set; }
    public int DaysRemainingInPeriod { get; set; }
    public decimal DailyAllowance { get; set; }
}

internal class AccountStatementRow
{
    public string Date { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

internal class NetWorthRow
{
    public string AccountName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
