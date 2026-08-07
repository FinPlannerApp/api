using Application;
using Application.DTOs.Merchants;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class MerchantsController : BaseController
{
    private readonly IMerchantService _service;

    public MerchantsController(IMerchantService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync(UserId);
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertMerchantDto dto)
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

    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string description)
    {
        var result = await _service.SuggestMerchantForDescriptionAsync(UserId, description);
        return HandleResult(result);
    }

    [HttpGet("spending")]
    public async Task<IActionResult> GetSpending([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var result = await _service.GetSpendingByMerchantAsync(UserId, startDate, endDate);
        return HandleResult(result);
    }
}
