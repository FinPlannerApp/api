using Application;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class ForecastController : BaseController
{
    private readonly IForecastService _service;
    public ForecastController(IForecastService service) => _service = service;

    [HttpGet("overview")] public async Task<IActionResult> Overview() => HandleResult(await _service.GetForecastAsync(UserId));
    [HttpGet("cards")] public async Task<IActionResult> Cards() => HandleResult(await _service.GetCardsAsync(UserId));
    [HttpGet("accounts")] public async Task<IActionResult> Accounts() => HandleResult(await _service.GetAccountsDashboardAsync(UserId));
}
