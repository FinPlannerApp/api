using Application;
using Application.DTOs.Goals;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class GoalsController : BaseController
{
    private readonly IGoalService _service;

    public GoalsController(IGoalService service)
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
    public async Task<IActionResult> Upsert([FromBody] UpsertGoalDto dto)
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
}
