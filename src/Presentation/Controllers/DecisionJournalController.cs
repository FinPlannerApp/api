using Application;
using Application.DTOs;
using Application.DTOs.DecisionJournal;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class DecisionJournalController : BaseController
{
    private readonly IDecisionJournalService _service;

    public DecisionJournalController(IDecisionJournalService service)
    {
        _service = service;
    }

    [HttpPost("search")]
    public async Task<IActionResult> GetPaged([FromBody] QueryParameters queryParams)
    {
        var result = await _service.GetPagedAsync(UserId, queryParams);
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertDecisionJournalEntryDto dto)
    {
        var result = await _service.UpsertAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto)
    {
        var result = await _service.DeleteAsync(UserId, dto.Id);
        return HandleResult(result);
    }

    [HttpPost("record-outcome")]
    public async Task<IActionResult> RecordOutcome([FromBody] RecordOutcomeDto dto)
    {
        var result = await _service.RecordOutcomeAsync(UserId, dto);
        return HandleResult(result);
    }
}
