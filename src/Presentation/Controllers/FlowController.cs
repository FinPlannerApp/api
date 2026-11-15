using Application;
using Application.DTOs.Flow;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class FlowController : BaseController
{
    private readonly IFlowService _service;
    public FlowController(IFlowService service) => _service = service;

    [HttpGet("obligations")]
    public async Task<IActionResult> Obligations() => HandleResult(await _service.GetObligationsAsync(UserId));

    [HttpPost("obligation-pause")]
    public async Task<IActionResult> Pause([FromBody] ObligationIdDto dto) => HandleResult(await _service.PauseAsync(UserId, dto.RecurringId));

    [HttpPost("obligation-resume")]
    public async Task<IActionResult> Resume([FromBody] ObligationIdDto dto) => HandleResult(await _service.ResumeAsync(UserId, dto.RecurringId));

    [HttpPost("obligation-cancel")]
    public async Task<IActionResult> Cancel([FromBody] ObligationIdDto dto) => HandleResult(await _service.CancelAsync(UserId, dto.RecurringId));

    [HttpPost("investment-withdraw-preview")]
    public async Task<IActionResult> Preview([FromBody] InvestmentWithdrawDto dto) => HandleResult(await _service.PreviewWithdrawalAsync(UserId, dto));

    [HttpPost("investment-withdraw")]
    public async Task<IActionResult> Withdraw([FromBody] InvestmentWithdrawDto dto) => HandleResult(await _service.WithdrawAsync(UserId, dto));
}
