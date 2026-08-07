using Application;
using Application.DTOs.SavingsBuckets;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class SavingsBucketsController : BaseController
{
    private readonly ISavingsBucketService _service;

    public SavingsBucketsController(ISavingsBucketService service)
    {
        _service = service;
    }

    [HttpGet("account/{accountId}")]
    public async Task<IActionResult> GetForAccount(int accountId)
    {
        var result = await _service.GetBucketsForAccountAsync(UserId, accountId);
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertSavingsBucketDto dto)
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

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllForUserAsync(UserId);
        return HandleResult(result);
    }
}
