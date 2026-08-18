using Application.Contracts;
using Application.DTOs.Split;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class SplitController : BaseController
{
    private readonly ISplitService _service;

    public SplitController(ISplitService service)
    {
        _service = service;
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
        => HandleResult(await _service.CreateGroupAsync(UserId, dto));

    [HttpGet("groups")]
    public async Task<IActionResult> GetMyGroups()
        => HandleResult(await _service.GetMyGroupsAsync(UserId));

    [HttpGet("groups/{groupId}")]
    public async Task<IActionResult> GetGroup(int groupId)
        => HandleResult(await _service.GetGroupAsync(UserId, groupId));

    [HttpPost("members")]
    public async Task<IActionResult> AddMember([FromBody] AddMemberDto dto)
        => HandleResult(await _service.AddMemberAsync(UserId, dto));

    [HttpPost("members/upi")]
    public async Task<IActionResult> UpdateMemberUpi([FromBody] UpdateMemberUpiDto dto)
        => HandleResult(await _service.UpdateMemberUpiAsync(UserId, dto));

    [HttpPost("expenses")]
    public async Task<IActionResult> AddExpense([FromBody] CreateExpenseDto dto)
        => HandleResult(await _service.AddExpenseAsync(UserId, dto));

    [HttpGet("groups/{groupId}/expenses")]
    public async Task<IActionResult> GetExpenses(int groupId)
        => HandleResult(await _service.GetExpensesAsync(UserId, groupId));

    [HttpGet("groups/{groupId}/balances")]
    public async Task<IActionResult> GetBalances(int groupId)
        => HandleResult(await _service.GetBalancesAsync(UserId, groupId));

    [HttpPost("settlements")]
    public async Task<IActionResult> CreateSettlement([FromBody] CreateSettlementDto dto)
        => HandleResult(await _service.CreateSettlementAsync(UserId, dto));

    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(int id, [FromBody] UpdateExpenseDto dto)
        => HandleResult(await _service.UpdateExpenseAsync(UserId, id, dto));

    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(int id)
        => HandleResult(await _service.DeleteExpenseAsync(UserId, id));

    [HttpPost("settlements/{id}/mark-sent")]
    public async Task<IActionResult> MarkPaymentSent(int id)
        => HandleResult(await _service.MarkPaymentSentAsync(UserId, id));

    [HttpPost("settlements/{id}/confirm-received")]
    public async Task<IActionResult> ConfirmPaymentReceived(int id)
        => HandleResult(await _service.ConfirmPaymentReceivedAsync(UserId, id));

    [HttpGet("settlements/{settlementId}/payment-request")]
    public async Task<IActionResult> GetPaymentRequest(int settlementId)
        => HandleResult(await _service.GetPaymentRequestAsync(UserId, settlementId));

    /// <summary>
    /// Public, no-login, read-only. The whole reason the shareable link
    /// exists — a trip participant without a FinPlanner account can open
    /// this and see balances/expenses, nothing more.
    /// </summary>
    [HttpGet("public/{shareToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicView(string shareToken)
        => HandleResult(await _service.GetPublicGroupViewAsync(shareToken));

    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteDto dto)
        => HandleResult(await _service.CreateInviteAsync(UserId, dto));

    [HttpGet("invites/{token}/preview")]
    [AllowAnonymous]
    public async Task<IActionResult> PreviewInvite(string token)
        => HandleResult(await _service.PreviewInviteAsync(token));

    [HttpPost("invites/join")]
    public async Task<IActionResult> JoinViaInvite([FromBody] JoinGroupDto dto)
        => HandleResult(await _service.JoinViaInviteAsync(UserId, dto));

    [HttpPost("invites/{inviteId}/revoke")]
    public async Task<IActionResult> RevokeInvite(int inviteId)
        => HandleResult(await _service.RevokeInviteAsync(UserId, inviteId));

    [HttpPost("groups/{groupId}/lock")]
    public async Task<IActionResult> LockGroup(int groupId)
        => HandleResult(await _service.LockGroupAsync(UserId, groupId));

    [HttpPost("import-to-ledger")]
    public async Task<IActionResult> ImportToLedger([FromBody] ImportToLedgerDto dto)
        => HandleResult(await _service.ImportToLedgerAsync(UserId, dto));

    [HttpGet("groups/{groupId}/settlements")]
    public async Task<IActionResult> GetSettlementHistory(int groupId)
        => HandleResult(await _service.GetSettlementHistoryAsync(UserId, groupId));
}
