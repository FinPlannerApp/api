using Application;
using Application.Common.Models;
using Application.DTOs;
using Application.DTOs.Budgets;
using Application.Features.Budgets.Commands;
using Application.Features.Budgets.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class BudgetsController : BaseController
{
    private readonly ISender _sender;

    public BudgetsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("search")]
    public async Task<IActionResult> GetPaged([FromBody] QueryParameters query)
    {
        var result = await _sender.Send(new GetBudgetsQuery(UserId, query));
        return HandleResult(result);
    }

    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress([FromQuery] DateTime? date)
    {
        var requestDate = date ?? DateTime.UtcNow;
        var result = await _sender.Send(new GetBudgetProgressQuery(UserId, requestDate));
        return HandleResult(result);
    }

    /// <summary>Where a budget's spend went: by sub-category (category budget) or by main category (overall budget).</summary>
    [HttpGet("{id:int}/breakdown")]
    public async Task<IActionResult> GetBreakdown(int id, [FromQuery] DateTime? date, [FromQuery] int? categoryId)
        => HandleResult(await _sender.Send(new GetBudgetBreakdownQuery(UserId, id, date ?? DateTime.UtcNow, categoryId)));

    [HttpGet("alert-patterns")]
    public async Task<IActionResult> GetAlertPatterns([FromQuery] int months = 6)
        => HandleResult(await _sender.Send(new GetBudgetAlertPatternsQuery(UserId, months)));

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] DateTime? date)
    {
        var result = await _sender.Send(new GetBudgetDashboardQuery(UserId, date ?? DateTime.UtcNow));
        return HandleResult(result);
    }

    public record AlertThresholdDto(int? AlertThresholdPercent);

    [HttpPut("{id:int}/alert-threshold")]
    public async Task<IActionResult> SetAlertThreshold(int id, [FromBody] AlertThresholdDto dto)
    {
        var result = await _sender.Send(new SetBudgetAlertThresholdCommand(UserId, id, dto.AlertThresholdPercent));
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertBudgetDto dto)
    {
        var result = await _sender.Send(new UpsertBudgetCommand(UserId, dto.Id, dto));
        return HandleResult(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertBudgetDto dto)
    {
        var result = await _sender.Send(new UpsertBudgetCommand(UserId, id, dto));
        return HandleResult(result);
    }

    // The generic ResourcePage in the UI deletes with POST {endpoint}/delete {id}; without this the call
    // hit no route and returned 405 (budgets could not be deleted from the list).
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteByBody([FromBody] DeleteDto dto)
    {
        var result = await _sender.Send(new DeleteBudgetCommand(UserId, dto.Id));
        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _sender.Send(new DeleteBudgetCommand(UserId, id));
        return HandleResult(result);
    }
}
