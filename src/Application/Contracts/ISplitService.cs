using Application.Common.Models;
using Application.DTOs.Split;

namespace Application.Contracts;

public interface ISplitService
{
    Task<Result<GroupDto>> CreateGroupAsync(string userId, CreateGroupDto dto);
    Task<Result<List<GroupDto>>> GetMyGroupsAsync(string userId);
    Task<Result<GroupDto>> GetGroupAsync(string userId, int groupId);
    Task<Result<MemberDto>> AddMemberAsync(string userId, AddMemberDto dto);
    Task<Result<bool>> UpdateMemberUpiAsync(string userId, UpdateMemberUpiDto dto);
    Task<Result<ExpenseDto>> AddExpenseAsync(string userId, CreateExpenseDto dto);
    Task<Result<List<ExpenseDto>>> GetExpensesAsync(string userId, int groupId);
    Task<Result<GroupBalancesDto>> GetBalancesAsync(string userId, int groupId);
    Task<Result<SettlementDto>> CreateSettlementAsync(string userId, CreateSettlementDto dto);
    Task<Result<bool>> MarkSettlementPaidAsync(string userId, int settlementId);
    Task<Result<PaymentRequestDto>> GetPaymentRequestAsync(string userId, int settlementId);
    Task<Result<PublicGroupViewDto>> GetPublicGroupViewAsync(string shareToken);
    Task<Result<InviteCreatedDto>> CreateInviteAsync(string userId, CreateInviteDto dto);
    Task<Result<InvitePreviewDto>> PreviewInviteAsync(string token);
    Task<Result<JoinGroupResultDto>> JoinViaInviteAsync(string userId, JoinGroupDto dto);
    Task<Result<bool>> RevokeInviteAsync(string userId, int inviteId);
    Task<Result<bool>> CloseGroupAsync(string userId, int groupId);
    Task<Result<ImportToLedgerResultDto>> ImportToLedgerAsync(string userId, ImportToLedgerDto dto);
    Task<Result<List<SettlementDto>>> GetSettlementHistoryAsync(string userId, int groupId);
}
