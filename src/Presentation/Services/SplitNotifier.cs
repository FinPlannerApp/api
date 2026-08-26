using Application.Contracts;
using API.Hubs;
using Domain.Entities.Split;
using Microsoft.AspNetCore.SignalR;

namespace API.Services;

public class SplitNotifier : ISplitNotifier
{
    private readonly IHubContext<SplitHub> _hubContext;

    public SplitNotifier(IHubContext<SplitHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyExpenseAddedAsync(int groupId, string activityMessage, object expense, object balances)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "ExpenseAdded",
            activityMessage,
            expense,
            balances
        });
    }

    public async Task NotifyExpenseUpdatedAsync(int groupId, string activityMessage, object expense, object balances)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "ExpenseUpdated",
            activityMessage,
            expense,
            balances
        });
    }

    public async Task NotifyExpenseDeletedAsync(int groupId, string activityMessage, int expenseId, object balances)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "ExpenseDeleted",
            activityMessage,
            expenseId,
            balances
        });
    }

    public async Task NotifySettlementRecordedAsync(int groupId, string activityMessage, object settlement, object balances)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "SettlementRecorded",
            activityMessage,
            settlement,
            balances
        });
    }

    public async Task NotifyMemberAddedAsync(int groupId, string activityMessage, object member)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "MemberAdded",
            activityMessage,
            member
        });
    }

    public async Task NotifyMemberUpiUpdatedAsync(int groupId, string activityMessage, int memberId, string? upiId)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "MemberUpiUpdated",
            activityMessage,
            memberId,
            upiId
        });
    }

    public async Task NotifyGroupStatusChangedAsync(int groupId, string activityMessage, SplitGroupStatus status)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "GroupStatusChanged",
            activityMessage,
            status = (int)status
        });
    }

    public async Task NotifyGroupUpdatedAsync(int groupId, string activityMessage)
    {
        await BroadcastAsync(groupId, new
        {
            groupId,
            eventType = "GroupUpdated",
            activityMessage
        });
    }

    private async Task BroadcastAsync(int groupId, object payload)
    {
        try
        {
            await _hubContext.Clients.Group(SplitHub.GetGroupName(groupId))
                .SendAsync("GroupUpdated", payload);
        }
        catch
        {
            // Fire-and-forget resilient broadcast
        }
    }
}
