using Application;
using Application.DTOs;
using Application.DTOs.Attachments;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class AttachmentsController : BaseController
{
    private readonly IAttachmentService _service;
    public AttachmentsController(IAttachmentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string entityType, [FromQuery] int entityId)
        => HandleResult(await _service.ListAsync(UserId, entityType, entityId));

    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] AddAttachmentDto dto) => HandleResult(await _service.AddAsync(UserId, dto));

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto) => HandleResult(await _service.DeleteAsync(UserId, dto.Id));
}
