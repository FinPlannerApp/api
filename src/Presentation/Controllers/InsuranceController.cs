using Application;
using Application.DTOs;
using Application.DTOs.Insurance;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class InsuranceController : BaseController
{
    private readonly IInsuranceService _service;
    public InsuranceController(IInsuranceService service) => _service = service;

    [HttpGet("overview")]
    public async Task<IActionResult> Overview() => HandleResult(await _service.GetOverviewAsync(UserId));

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertInsurancePolicyDto dto) => HandleResult(await _service.UpsertAsync(UserId, dto));

    [HttpPost("mark-paid")]
    public async Task<IActionResult> MarkPaid([FromBody] DeleteDto dto) => HandleResult(await _service.MarkPaidAsync(UserId, dto.Id));

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto) => HandleResult(await _service.DeleteAsync(UserId, dto.Id));
}
