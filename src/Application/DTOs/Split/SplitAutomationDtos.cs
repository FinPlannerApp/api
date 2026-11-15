using Domain.Entities.Split;

namespace Application.DTOs.Split;

public class CreateRecurringExpenseDto
{
    public int GroupId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public SplitType SplitType { get; set; }
    public string? Notes { get; set; }
    public List<ExpensePayerDto> Payers { get; set; } = new();
    public List<ExpenseParticipantDto> Participants { get; set; } = new();
    public SplitRecurrenceFrequency Frequency { get; set; }
    /// <summary>The first date an expense is added.</summary>
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// True when the expense for <see cref="StartDate"/> was just added by the caller, so the
    /// schedule starts with the occurrence after it (no duplicate on the first day).
    /// </summary>
    public bool FirstAlreadyAdded { get; set; }
}

public class RecurringExpenseDto
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public SplitRecurrenceFrequency Frequency { get; set; }
    public DateTime NextRunDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
    public int RunCount { get; set; }
    public string PaidBy { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
}

public class SetActiveDto
{
    public bool Active { get; set; }
}

public class SendReminderDto
{
    public int GroupId { get; set; }
    /// <summary>The member who owes money and should be reminded.</summary>
    public int FromMemberId { get; set; }
}

public class ReminderResultDto
{
    public string SentTo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
