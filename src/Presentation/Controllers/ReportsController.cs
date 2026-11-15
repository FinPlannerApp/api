using Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// File-download endpoints — deliberately does NOT use BaseController.HandleResult,
/// since that wraps every response in the ApiResult JSON envelope. A CSV
/// download needs to return the raw file with a text/csv content type, not
/// JSON containing a byte array.
/// </summary>
[Route("api/[controller]")]
public class ReportsController : BaseController
{
    private readonly IReportService _reportService;
    private readonly IPeriodReportService _periodReports;

    public ReportsController(IReportService reportService, IPeriodReportService periodReports)
    {
        _reportService = reportService;
        _periodReports = periodReports;
    }

    /// <summary>On-screen report for one month (month 1-12) or a whole year (omit month). Nothing is generated or stored.</summary>
    [HttpGet("period")]
    public async Task<IActionResult> Period([FromQuery] int year, [FromQuery] int? month)
        => HandleResult(await _periodReports.GetAsync(UserId, year, month));

    /// <summary>Excel file of the same report, built only when the user asks for it.</summary>
    [HttpGet("period/excel")]
    public async Task<IActionResult> PeriodExcel([FromQuery] int year, [FromQuery] int? month)
    {
        var result = await _periodReports.ExportExcelAsync(UserId, year, month);
        if (!result.IsSuccess) return HandleResult(result);
        var name = month.HasValue ? $"report-{year}-{month:D2}.xlsx" : $"report-{year}.xlsx";
        return File(result.Value!, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>PDF of the same report, built only when the user asks for it.</summary>
    [HttpGet("period/pdf")]
    public async Task<IActionResult> PeriodPdf([FromQuery] int year, [FromQuery] int? month)
    {
        var result = await _periodReports.ExportPdfAsync(UserId, year, month);
        if (!result.IsSuccess) return HandleResult(result);
        var name = month.HasValue ? $"report-{year}-{month:D2}.pdf" : $"report-{year}.pdf";
        return File(result.Value!, "application/pdf", name);
    }

    [HttpGet("monthly-summary")]
    public async Task<IActionResult> MonthlySummary([FromQuery] int month, [FromQuery] int year)
    {
        var result = await _reportService.GenerateMonthlySummaryReportAsync(UserId, month, year);
        if (!result.IsSuccess)
            return BadRequest(result.Error.Description);

        return File(result.Value!, "text/csv", $"monthly-summary-{year}-{month:D2}.csv");
    }

    [HttpGet("category-analysis")]
    public async Task<IActionResult> CategoryAnalysis([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var result = await _reportService.GenerateCategoryAnalysisReportAsync(UserId, startDate, endDate);
        if (!result.IsSuccess)
            return BadRequest(result.Error.Description);

        return File(result.Value!, "text/csv", $"category-analysis-{startDate:yyyy-MM-dd}-to-{endDate:yyyy-MM-dd}.csv");
    }

    [HttpGet("budget-vs-actual")]
    public async Task<IActionResult> BudgetVsActual([FromQuery] DateTime? asOfDate)
    {
        var result = await _reportService.GenerateBudgetVsActualReportAsync(UserId, asOfDate ?? DateTime.UtcNow);
        if (!result.IsSuccess)
            return BadRequest(result.Error.Description);

        return File(result.Value!, "text/csv", $"budget-vs-actual-{(asOfDate ?? DateTime.UtcNow):yyyy-MM-dd}.csv");
    }

    [HttpGet("account-statement/{accountId}")]
    public async Task<IActionResult> AccountStatement(int accountId, [FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var result = await _reportService.GenerateAccountStatementReportAsync(UserId, accountId, startDate, endDate);
        if (!result.IsSuccess)
            return BadRequest(result.Error.Description);

        return File(result.Value!, "text/csv", $"account-statement-{accountId}-{startDate:yyyy-MM-dd}-to-{endDate:yyyy-MM-dd}.csv");
    }

    [HttpGet("net-worth")]
    public async Task<IActionResult> NetWorth()
    {
        var result = await _reportService.GenerateNetWorthReportAsync(UserId);
        if (!result.IsSuccess)
            return BadRequest(result.Error.Description);

        return File(result.Value!, "text/csv", $"net-worth-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
