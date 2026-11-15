using Application;
using Application.DTOs;
using Application.DTOs.Transactions;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/transaction-rules")]
public class TransactionRulesController : BaseController
{
    private readonly ITransactionRuleService _service;
    public TransactionRulesController(ITransactionRuleService service) { _service = service; }

    [HttpGet]
    public async Task<IActionResult> GetAll() => HandleResult(await _service.GetAllAsync(UserId));

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertTransactionRuleDto dto) => HandleResult(await _service.UpsertAsync(UserId, dto));

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto) => HandleResult(await _service.DeleteAsync(UserId, dto.Id));

    /// <summary>Which rule (if any) would apply to a transaction like this; used to pre-fill the form.</summary>
    [HttpPost("match")]
    public async Task<IActionResult> Match([FromBody] RuleMatchRequestDto dto) => HandleResult(await _service.MatchAsync(UserId, dto));
}
