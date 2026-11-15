using Application.Common.Models;
using Application.DTOs.Split;

namespace Application.Contracts;

public interface ISplitAutomationService
{
    Task<Result<RecurringExpenseDto>> CreateRecurringAsync(string userId, CreateRecurringExpenseDto dto);
    Task<Result<List<RecurringExpenseDto>>> GetRecurringAsync(string userId, int groupId);
    Task<Result<bool>> SetRecurringActiveAsync(string userId, int recurringId, bool active);
    Task<Result<bool>> DeleteRecurringAsync(string userId, int recurringId);
    Task<Result<ReminderResultDto>> SendReminderAsync(string userId, SendReminderDto dto);

    /// <summary>Background job: adds every recurring expense that is due on or before <paramref name="todayUtc"/>. Returns how many were added.</summary>
    Task<int> RunDueRecurringAsync(DateTime todayUtc);

    /// <summary>Background job: emails weekly reminders for groups that turned them on. Returns how many emails were sent.</summary>
    Task<int> SendAutoRemindersAsync(DateTime nowUtc);
}
