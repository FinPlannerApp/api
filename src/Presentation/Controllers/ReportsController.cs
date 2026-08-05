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

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
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
