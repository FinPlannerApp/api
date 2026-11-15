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

    /// <summary>The user's most repeated entries, offered as one-tap Quick Cards in Bulk add.</summary>
    [HttpGet("quick-cards")]
    public async Task<IActionResult> GetQuickCards([FromQuery] int days = 120, [FromQuery] int minCount = 3, [FromQuery] int limit = 12)
        => HandleResult(await _sender.Send(new GetQuickCardsQuery(UserId, days, minCount, limit)));

    /// <summary>Expense total per day between two dates (spending heatmap).</summary>
    [HttpGet("daily-spend")]
    public async Task<IActionResult> GetDailySpend([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
        => HandleResult(await _sender.Send(new GetDailySpendQuery(UserId, startDate, endDate)));

    /// <summary>Tags the user has used, with counts.</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags() => HandleResult(await _sender.Send(new GetTransactionTagsQuery(UserId)));

    /// <summary>Deleted transactions that can still be undone, with the effect of undoing each.</summary>
    [HttpGet("undoable")]
    public async Task<IActionResult> GetUndoable()
        => HandleResult(await _sender.Send(new GetUndoableTransactionsQuery(UserId)));

    /// <summary>Undo a delete made in the last few minutes (plain transactions only).</summary>
    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] DeleteTransactionDto dto)
        => HandleResult(await _sender.Send(new RestoreTransactionCommand(UserId, dto.AccountId, dto.TransactionId)));

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

    /// <summary>Statement import, step 1: compare parsed statement rows with saved entries. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> ImportPreview([FromBody] StatementImportPreviewRequestDto dto)
        => HandleResult(await _sender.Send(new PreviewStatementImportCommand(UserId, dto)));

    /// <summary>Statement import, step 2: add the new rows and mark matched entries as reconciled.</summary>
    [HttpPost("import/commit")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> ImportCommit([FromBody] StatementImportCommitDto dto)
        => HandleResult(await _sender.Send(new CommitStatementImportCommand(UserId, dto)));
}
