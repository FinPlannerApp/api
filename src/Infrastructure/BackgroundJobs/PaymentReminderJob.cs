using System.Text.Json;
using Application.Common.Helpers;
using Application.Contracts;
using Application.DTOs.Settings;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Daily email digest of payments coming due. Does nothing for anyone who has not switched
/// "Payment reminders" on (Settings → Notifications; off by default).
/// </summary>
public class PaymentReminderJob
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<PaymentReminderJob> _logger;

    public PaymentReminderJob(IApplicationDbContext context, IEmailService email, ILogger<PaymentReminderJob> logger)
    {
        _context = context;
        _email = email;
        _logger = logger;
    }

    public async Task RunAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var today = (nowUtc ?? DateTime.UtcNow).Date;
        var rows = await _context.UserModuleSettings.AsNoTracking()
            .Where(s => s.Module == "notifications")
            .Select(s => new { s.UserId, s.DataJson })
            .ToListAsync(ct);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var row in rows)
        {
            NotificationSettingsDto? settings;
            try { settings = JsonSerializer.Deserialize<NotificationSettingsDto>(row.DataJson, opts); }
            catch { continue; }
            if (settings is not { PaymentReminderEmails: true }) continue;

            try { await RemindUserAsync(row.UserId, today, Math.Clamp(settings.ReminderDaysBefore, 1, 14), ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Payment reminder failed for one user; continuing."); }
        }
    }

    private async Task RemindUserAsync(string userId, DateTime today, int days, CancellationToken ct)
    {
        var end = today.AddDays(days).AddDays(1);          // exclusive
        var items = new List<(string Key, ReminderItem Item)>();

        var recurring = await _context.RecurringTransactions.AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive && r.Type == TransactionType.Expense
                        && r.NextProcessDate >= today && r.NextProcessDate < end
                        && (r.EndDate == null || r.EndDate >= r.NextProcessDate))
            .Select(r => new { r.Id, r.Description, r.Amount, r.NextProcessDate })
            .ToListAsync(ct);
        var recIds = recurring.Select(r => r.Id).ToList();
        var subNames = await _context.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && recIds.Contains(s.RecurringTransactionId))
            .Select(s => new { s.RecurringTransactionId, s.Name })
            .ToDictionaryAsync(s => s.RecurringTransactionId, s => s.Name, ct);
        foreach (var r in recurring)
        {
            var isSub = subNames.TryGetValue(r.Id, out var name);
            items.Add(($"rec:{r.Id}", new ReminderItem(isSub ? name! : r.Description, r.NextProcessDate, r.Amount, isSub ? "subscription" : "recurring")));
        }

        var policies = await _context.InsurancePolicies.AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive && p.NextDueDate != null && p.NextDueDate >= today && p.NextDueDate < end)
            .Select(p => new { p.Id, p.Name, p.Premium, p.NextDueDate })
            .ToListAsync(ct);
        foreach (var p in policies)
            items.Add(($"ins:{p.Id}", new ReminderItem(p.Name, p.NextDueDate!.Value, p.Premium, "insurance premium")));

        var bills = await _context.CreditCardBills.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsPaid && b.DueDate != null && b.DueDate >= today && b.DueDate < end)
            .Select(b => new { b.Id, AccountName = b.Account.Name, b.BillAmount, b.DueDate })
            .ToListAsync(ct);
        foreach (var b in bills)
            items.Add(($"bill:{b.Id}", new ReminderItem(b.AccountName + " bill", b.DueDate!.Value, b.BillAmount, "card bill")));

        if (items.Count == 0) return;

        var keys = items.Select(i => i.Key).Distinct().ToList();
        var already = await _context.ReminderLogs.AsNoTracking()
            .Where(l => l.UserId == userId && keys.Contains(l.RefKey))
            .Select(l => new { l.RefKey, l.DueDate })
            .ToListAsync(ct);
        var sent = already.Select(a => (a.RefKey, a.DueDate.Date)).ToHashSet();
        var fresh = items.Where(i => !sent.Contains((i.Key, i.Item.DueDate.Date))).ToList();
        if (fresh.Count == 0) return;

        var to = await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        var mail = ReminderEmailBuilder.Build(to ?? string.Empty, fresh.Select(f => f.Item).ToList(), today);
        if (mail == null) return;

        await _email.SendEmailAsync(mail);
        foreach (var f in fresh)
            _context.ReminderLogs.Add(new ReminderLog { UserId = userId, RefKey = f.Key, DueDate = f.Item.DueDate.Date });
        await _context.SaveChangesAsync(ct);
    }
}
