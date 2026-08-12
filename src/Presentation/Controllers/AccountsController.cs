using Application;
using Application.Contracts;
using Application.DTOs;
using Application.DTOs.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[Authorize]
public class AccountsController : BaseController
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _accountService.GetAllAccountsAsync(UserId);
        return HandleResult(result);
    }

    [HttpPost("search")] // Paginated view
    public async Task<IActionResult> GetPaged([FromBody] QueryParameters queryParams)
    {
        var result = await _accountService.GetPagedAccountsAsync(UserId, queryParams);
        return HandleResult(result);
    }

    [HttpPost("upsert")] // Create and Update
    public async Task<IActionResult> Upsert([FromBody] UpsertAccountDto dto)
    {
        var result = await _accountService.UpsertAccountAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto)
    {
        var result = await _accountService.DeleteAccountAsync(UserId, dto.Id);
        return HandleResult(result);
    }

    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeAccountsDto dto)
    {
        var result = await _accountService.MergeAccountsAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(int id)
    {
        var result = await _accountService.SetArchivedStatusAsync(UserId, id, true);
        return HandleResult(result);
    }

    [HttpPost("{id}/unarchive")]
    public async Task<IActionResult> Unarchive(int id)
    {
        var result = await _accountService.SetArchivedStatusAsync(UserId, id, false);
        return HandleResult(result);
    }

    [HttpPost("loan-payment")]
    public async Task<IActionResult> MakeLoanPayment([FromBody] MakeLoanPaymentDto dto)
    {
        var result = await _accountService.MakeLoanPaymentAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpGet("{loanAccountId}/amortization-schedule")]
    public async Task<IActionResult> GetAmortizationSchedule(int loanAccountId)
    {
        var result = await _accountService.GetAmortizationScheduleAsync(UserId, loanAccountId);
        return HandleResult(result);
    }

    [HttpGet("{accountId}/credit-card-breakdown")]
    public async Task<IActionResult> GetCreditCardBreakdown(int accountId)
    {
        var result = await _accountService.GetCreditCardBreakdownAsync(UserId, accountId);
        return HandleResult(result);
    }

    [HttpPost("adjust-balance")]
    public async Task<IActionResult> AdjustBalance([FromBody] AdjustBalanceDto dto)
    {
        var result = await _accountService.AdjustBalanceAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("record-credit-card-bill")]
    public async Task<IActionResult> RecordCreditCardBill([FromBody] RecordCreditCardBillDto dto)
    {
        var result = await _accountService.RecordCreditCardBillAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("backfill-opening-balances")]
    public async Task<IActionResult> BackfillOpeningBalances()
    {
        var result = await _accountService.BackfillOpeningBalancesAsync(UserId);
        return HandleResult(result);
    }
}
