using Application.Common.Models;
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

    /// <summary>
    /// Paged expense list, newest day first by default. Pass the previous response's
    /// nextCursor as <c>cursor</c> to load the next page (infinite scroll). Optional
    /// filters: search, category, payerId, participantId, from, to, minAmount, maxAmount;
    /// sort = date_desc | date_asc | amount_desc | amount_asc.
    /// </summary>
    [HttpGet("groups/{groupId}/expenses")]
    public async Task<IActionResult> GetExpenses(int groupId, [FromQuery] ExpenseListQuery query)
        => HandleResult(await _service.GetExpensesAsync(UserId, groupId, query));

    [HttpGet("groups/{groupId}/export")]
    public async Task<IActionResult> GetGroupExport(int groupId)
        => HandleResult(await _service.GetGroupExportAsync(UserId, groupId));

    [HttpGet("groups/{groupId}/balances")]
    public async Task<IActionResult> GetBalances(int groupId)
        => HandleResult(await _service.GetBalancesAsync(UserId, groupId));

    [HttpGet("groups/{groupId}/full")]
    public async Task<IActionResult> GetGroupFullDetails(int groupId)
        => HandleResult(await _service.GetGroupFullDetailsAsync(UserId, groupId));

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

    [HttpPost("settlements/{id}/reject")]
    public async Task<IActionResult> RejectPayment(int id)
        => HandleResult(await _service.RejectPaymentAsync(UserId, id));

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

    [HttpGet("public/{shareToken}/expenses")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicExpenses(
    string shareToken, [FromQuery] string? cursor, [FromQuery] int? limit)
    => HandleResult(await _service.GetPublicExpensesAsync(shareToken, cursor, limit));

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

    [HttpPost("groups/{groupId}/unlock")]
    public async Task<IActionResult> UnlockGroup(int groupId)
        => HandleResult(await _service.UnlockGroupAsync(UserId, groupId));

    [HttpPost("groups/{groupId}/close")]
    public async Task<IActionResult> CloseGroup(int groupId)
        => HandleResult(await _service.CloseGroupAsync(UserId, groupId));

    [HttpPut("groups/{groupId}")]
    public async Task<IActionResult> UpdateGroup(int groupId, [FromBody] UpdateGroupDto dto)
    {
        dto.GroupId = groupId;
        return HandleResult(await _service.UpdateGroupAsync(UserId, dto));
    }

    [HttpPost("members/rename")]
    public async Task<IActionResult> RenameMember([FromBody] RenameMemberDto dto)
        => HandleResult(await _service.RenameMemberAsync(UserId, dto));

    [HttpPost("import-to-ledger")]
    public async Task<IActionResult> ImportToLedger([FromBody] ImportToLedgerDto dto)
        => HandleResult(await _service.ImportToLedgerAsync(UserId, dto));

    [HttpGet("groups/{groupId}/settlements")]
    public async Task<IActionResult> GetSettlementHistory(int groupId)
        => HandleResult(await _service.GetSettlementHistoryAsync(UserId, groupId));

    /// <summary>Leave the group (yourself) or remove a member (admin). Needs a zero balance.</summary>
    [HttpDelete("members/{memberId}")]
    public async Task<IActionResult> RemoveMember(int memberId)
        => HandleResult(await _service.RemoveMemberAsync(UserId, memberId));

    [HttpDelete("groups/{groupId}")]
    public async Task<IActionResult> DeleteGroup(int groupId)
        => HandleResult(await _service.DeleteGroupAsync(UserId, groupId));

    [HttpPut("settlements/{id}")]
    public async Task<IActionResult> UpdateSettlement(int id, [FromBody] UpdateSettlementDto dto)
        => HandleResult(await _service.UpdateSettlementAsync(UserId, id, dto));

    [HttpDelete("settlements/{id}")]
    public async Task<IActionResult> DeleteSettlement(int id)
        => HandleResult(await _service.DeleteSettlementAsync(UserId, id));

    /// <summary>The group's history / audit trail, newest first.</summary>
    [HttpGet("groups/{groupId}/activity")]
    public async Task<IActionResult> GetActivity(int groupId, [FromQuery] string? cursor, [FromQuery] int? limit)
        => HandleResult(await _service.GetActivityAsync(UserId, groupId, cursor, limit));

    /// <summary>Recently deleted expenses, for "undo".</summary>
    [HttpGet("groups/{groupId}/expenses/deleted")]
    public async Task<IActionResult> GetDeletedExpenses(int groupId)
        => HandleResult(await _service.GetDeletedExpensesAsync(UserId, groupId));

    [HttpPost("expenses/{id}/restore")]
    public async Task<IActionResult> RestoreExpense(int id)
        => HandleResult(await _service.RestoreExpenseAsync(UserId, id));

    /// <summary>Search expenses across every group the caller belongs to.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchAcrossGroups([FromQuery] string q, [FromQuery] int? limit)
        => HandleResult(await _service.SearchExpensesAcrossGroupsAsync(UserId, q, limit));

    private static readonly Dictionary<string, string> ReceiptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private const long MaxReceiptBytes = 5 * 1024 * 1024;
    private const string ReceiptUrlPrefix = "/uploads/split-receipts/";

    /// <summary>Attach (or replace) a receipt photo: JPG, PNG or WebP up to 5 MB.</summary>
    [HttpPost("expenses/{id}/receipt")]
    [RequestSizeLimit(MaxReceiptBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(int id, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return ReceiptError("Choose an image to upload.");
        if (file.Length > MaxReceiptBytes)
            return ReceiptError("The receipt must be 5 MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (!ReceiptTypes.TryGetValue(extension, out var expectedType) ||
            !string.Equals(file.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
            return ReceiptError("Only JPG, PNG or WebP images are allowed.");

        // Random server-side name: the caller's file name is never used on disk.
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "split-receipts", id.ToString());
        Directory.CreateDirectory(folder);
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(folder, storedName);
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew))
        {
            await file.CopyToAsync(stream);
        }

        var relative = $"{ReceiptUrlPrefix}{id}/{storedName}";
        var result = await _service.SetReceiptAsync(UserId, id, relative);
        if (!result.IsSuccess)
        {
            System.IO.File.Delete(fullPath); // access refused — don't keep the upload
            return HandleResult(result);
        }

        DeleteReceiptFile(result.Value.PreviousPath);
        return HandleResult(result);
    }

    [HttpDelete("expenses/{id}/receipt")]
    public async Task<IActionResult> RemoveReceipt(int id)
    {
        var result = await _service.SetReceiptAsync(UserId, id, null);
        if (result.IsSuccess) DeleteReceiptFile(result.Value.PreviousPath);
        return HandleResult(result);
    }

    private IActionResult ReceiptError(string message)
        => HandleResult(Result.Failure<ReceiptChangeDto>(new Error("SplitReceipt.Invalid", message)));

    private static void DeleteReceiptFile(string? relativePath)
    {
        // Only ever touch files we created ourselves, under the receipts folder.
        if (string.IsNullOrEmpty(relativePath) || !relativePath.StartsWith(ReceiptUrlPrefix, StringComparison.Ordinal) ||
            relativePath.Contains(".."))
            return;

        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "split-receipts"));
        var full = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/')));
        if (full.StartsWith(root, StringComparison.Ordinal) && System.IO.File.Exists(full))
            System.IO.File.Delete(full);
    }
}
