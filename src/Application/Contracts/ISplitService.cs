using Application.Common.Models;
using Application.DTOs.Split;

namespace Application.Contracts;

public interface ISplitService
{
    Task<Result<GroupDto>> CreateGroupAsync(string userId, CreateGroupDto dto);
    Task<Result<List<GroupDto>>> GetMyGroupsAsync(string userId);
    Task<Result<GroupDto>> GetGroupAsync(string userId, int groupId);
    Task<Result<GroupFullDetailsDto>> GetGroupFullDetailsAsync(string userId, int groupId);
    Task<Result<MemberDto>> AddMemberAsync(string userId, AddMemberDto dto);
    Task<Result<bool>> UpdateMemberUpiAsync(string userId, UpdateMemberUpiDto dto);
    Task<Result<ExpenseDto>> AddExpenseAsync(string userId, CreateExpenseDto dto);
    Task<Result<ExpensePageDto>> GetExpensesAsync(string userId, int groupId, ExpenseListQuery query);
    Task<Result<ExpensePageDto>> GetPublicExpensesAsync(string shareToken, string? cursor, int? limit);
    Task<Result<GroupExportDto>> GetGroupExportAsync(string userId, int groupId);
    Task<Result<GroupBalancesDto>> GetBalancesAsync(string userId, int groupId);
    Task<Result<SettlementDto>> CreateSettlementAsync(string userId, CreateSettlementDto dto);
    Task<Result<ExpenseDto>> UpdateExpenseAsync(string userId, int expenseId, UpdateExpenseDto dto);
    Task<Result<DeleteExpenseResultDto>> DeleteExpenseAsync(string userId, int expenseId);
    Task<Result<bool>> MarkPaymentSentAsync(string userId, int settlementId);
    Task<Result<bool>> ConfirmPaymentReceivedAsync(string userId, int settlementId);
    Task<Result<bool>> RejectPaymentAsync(string userId, int settlementId);
    Task<Result<PaymentRequestDto>> GetPaymentRequestAsync(string userId, int settlementId);
    Task<Result<PublicGroupViewDto>> GetPublicGroupViewAsync(string shareToken);
    Task<Result<InviteCreatedDto>> CreateInviteAsync(string userId, CreateInviteDto dto);
    Task<Result<InvitePreviewDto>> PreviewInviteAsync(string token);
    Task<Result<JoinGroupResultDto>> JoinViaInviteAsync(string userId, JoinGroupDto dto);
    Task<Result<bool>> RevokeInviteAsync(string userId, int inviteId);
    Task<Result<GroupDto>> UpdateGroupAsync(string userId, UpdateGroupDto dto);
    Task<Result<MemberDto>> RenameMemberAsync(string userId, RenameMemberDto dto);
    Task<Result<bool>> UnlockGroupAsync(string userId, int groupId);
    Task<Result<bool>> CloseGroupAsync(string userId, int groupId);
    Task<Result<bool>> LockGroupAsync(string userId, int groupId);
    Task<Result<ImportToLedgerResultDto>> ImportToLedgerAsync(string userId, ImportToLedgerDto dto);
    Task<Result<List<SettlementDto>>> GetSettlementHistoryAsync(string userId, int groupId);
    Task<Result<bool>> RemoveMemberAsync(string userId, int memberId);
    Task<Result<bool>> DeleteGroupAsync(string userId, int groupId);
    Task<Result<ActivityPageDto>> GetActivityAsync(string userId, int groupId, string? cursor, int? limit);
    Task<Result<List<ExpenseDto>>> GetDeletedExpensesAsync(string userId, int groupId);
    Task<Result<ExpenseDto>> RestoreExpenseAsync(string userId, int expenseId);
    Task<Result<ReceiptChangeDto>> SetReceiptAsync(string userId, int expenseId, string? receiptPath);
    Task<Result<List<GlobalExpenseHitDto>>> SearchExpensesAcrossGroupsAsync(string userId, string query, int? limit);
    Task<Result<SettlementDto>> UpdateSettlementAsync(string userId, int settlementId, UpdateSettlementDto dto);
    Task<Result<bool>> DeleteSettlementAsync(string userId, int settlementId);
}
