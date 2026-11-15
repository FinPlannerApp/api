namespace Domain.Entities;

/// <summary>A payment reminder email that was already sent (so the same due date is never emailed twice).</summary>
public class ReminderLog
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary>"rec:12" recurring item, "ins:3" insurance policy, "bill:7" card bill.</summary>
    public string RefKey { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
