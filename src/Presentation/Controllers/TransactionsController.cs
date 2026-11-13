using Application;
using Application.DTOs.Transactions;
using Application.Features.Transactions.Commands;
using Application.Features.Transactions.Queries;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[Route("api/[controller]")]
public class TransactionsController : BaseController
{
    private readonly ISender _sender;

    public TransactionsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("search")]
    public async Task<IActionResult> GetPaged([FromBody] TransactionSearchDto dto)
    {
        var result = await _sender.Send(new GetTransactionsQuery(UserId, dto.AccountId, dto.QueryParameters));
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertTransactionPayloadDto dto)
    {
        var result = await _sender.Send(new UpsertTransactionCommand(UserId, dto.AccountId, dto.Transaction));
        return HandleResult(result);
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteTransactionDto dto)
    {
        var result = await _sender.Send(new DeleteTransactionCommand(UserId, dto.AccountId, dto.TransactionId));
        return HandleResult(result);
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> CreateTransfer([FromBody] CreateTransferPayloadDto dto)
    {
        var result = await _sender.Send(new CreateTransferCommand(UserId, dto.AccountId, dto.Transfer));
        return HandleResult(result);
    }

    [HttpPost("switch-account")]
    public async Task<IActionResult> SwitchAccount([FromBody] SwitchTransactionAccountPayloadDto dto)
    {
        var result = await _sender.Send(new SwitchAccountCommand(UserId, dto.TransactionId, dto.DestinationAccountId));
        return HandleResult(result);
    }

    /// <summary>Dry run of a bulk add: reports rows that would fail and likely duplicates. Writes nothing.</summary>
    [HttpPost("bulk-validate")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> BulkValidate([FromBody] BulkTransactionPayloadDto dto)
        => HandleResult(await _sender.Send(new ValidateBulkTransactionsCommand(UserId, dto)));

    [HttpPost("bulk-upsert")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> BulkUpsert([FromBody] BulkTransactionPayloadDto dto)
    {
        var result = await _sender.Send(new BulkUpsertTransactionsCommand(UserId, dto));
        return HandleResult(result);
    }
}
