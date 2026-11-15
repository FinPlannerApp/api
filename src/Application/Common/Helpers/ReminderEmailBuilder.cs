using System.Net;
using Application.DTOs.Auth;

namespace Application.Common.Helpers;

public record ReminderItem(string Label, DateTime DueDate, decimal? Amount, string Kind);

/// <summary>Builds the reminder digest email (pure, so it can be unit tested). All text from the user's data is HTML-encoded.</summary>
public static class ReminderEmailBuilder
{
    public static MailRequest? Build(string to, IReadOnlyCollection<ReminderItem> items, DateTime today)
    {
        if (items.Count == 0 || string.IsNullOrWhiteSpace(to)) return null;
        var ordered = items.OrderBy(i => i.DueDate).ThenBy(i => i.Label).ToList();
        var subject = ordered.Count == 1
            ? $"Reminder: {ordered[0].Label} {When(ordered[0].DueDate, today)}"
            : $"Reminder: {ordered.Count} payments coming up";
        var rows = string.Concat(ordered.Select(i =>
            $"<li><strong>{WebUtility.HtmlEncode(i.Label)}</strong> ({WebUtility.HtmlEncode(i.Kind)}) — {When(i.DueDate, today)}" +
            (i.Amount.HasValue ? $", ₹{i.Amount.Value:N2}" : "") + "</li>"));
        var body = $"<p>These payments are due soon:</p><ul>{rows}</ul>" +
                   "<p style=\"color:#666;font-size:12px\">You get this because payment reminders are switched on in FinPlanner → Settings → Notifications. You can switch them off there at any time.</p>";
        return new MailRequest { To = to, Subject = subject, Body = body };
    }

    public static string When(DateTime due, DateTime today)
    {
        var d = (due.Date - today.Date).Days;
        return d <= 0 ? "due today" : d == 1 ? "due tomorrow" : $"due on {due:d MMM}";
    }
}
