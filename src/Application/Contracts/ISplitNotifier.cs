using Domain.Entities.Split;

namespace Application.Contracts;

public interface ISplitNotifier
{
    Task NotifyExpenseAddedAsync(int groupId, string activityMessage, object expense, object balances);
    Task NotifyExpenseUpdatedAsync(int groupId, string activityMessage, object expense, object balances);
    Task NotifyExpenseDeletedAsync(int groupId, string activityMessage, int expenseId, object balances);
    Task NotifySettlementRecordedAsync(int groupId, string activityMessage, object settlement, object balances);
    Task NotifyMemberAddedAsync(int groupId, string activityMessage, object member);
    Task NotifyMemberUpiUpdatedAsync(int groupId, string activityMessage, int memberId, string? upiId);
    Task NotifyGroupStatusChangedAsync(int groupId, string activityMessage, SplitGroupStatus status);
    Task NotifyGroupUpdatedAsync(int groupId, string activityMessage);
}
