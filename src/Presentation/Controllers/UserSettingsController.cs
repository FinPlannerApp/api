using System.Text.Json;
using Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/user-settings")]
public class UserSettingsController : BaseController
{
    private readonly IUserSettingsService _settings;

    public UserSettingsController(IUserSettingsService settings) => _settings = settings;

    [HttpGet("{module}")]
    public async Task<IActionResult> Get(string module, CancellationToken ct)
        => HandleResult(await _settings.GetAsync(UserId, module, ct));

    [HttpPut("{module}")]
    public async Task<IActionResult> Put(string module, [FromBody] JsonElement body, CancellationToken ct)
        => HandleResult(await _settings.SaveAsync(UserId, module, body, ct));
}
