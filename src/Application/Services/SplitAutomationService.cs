using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Auth;
using Application.DTOs.Split;
using Domain.Entities;
using Domain.Entities.Split;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>Recurring expenses and payment reminders for split groups.</summary>
public class SplitAutomationService : ISplitAutomationService
{
    private const int MaxRecurringPerGroup = 20;
    private const int MaxCatchUpPerRun = 12;

    private readonly IApplicationDbContext _context;
    private readonly ISplitService _splitService;
    private readonly IEmailService _email;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _config;
    private readonly ILogger<SplitAutomationService> _logger;

    public SplitAutomationService(
        IApplicationDbContext context,
        ISplitService splitService,
        IEmailService email,
        UserManager<ApplicationUser> users,
        IConfiguration config,
        ILogger<SplitAutomationService> logger)
    {
        _context = context;
        _splitService = splitService;
        _email = email;
        _users = users;
        _config = config;
        _logger = logger;
    }

    // ── Recurring expenses ───────────────────────────────────────────────────

    public async Task<Result<RecurringExpenseDto>> CreateRecurringAsync(string userId, CreateRecurringExpenseDto dto)
    {
        var group = await _context.SplitGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == dto.GroupId);
        if (group == null || !IsActiveMember(group, userId))
            return Result.Failure<RecurringExpenseDto>(new Error("SplitGroup.NotFound", "Group not found."));
        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitGroup.Closed", "This group is closed."));
        if (group.CreatedByUserId != userId && group.Members.Any(m => m.LinkedUserId == userId && m.LeftAt == null && m.IsGuest && !m.IsAdmin))
            return Result.Failure<RecurringExpenseDto>(new Error("SplitGroup.Forbidden", "Guests can view this group but can't add expenses."));

        if (string.IsNullOrWhiteSpace(dto.Description))
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "Add a description."));
        if (dto.Amount <= 0)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "Amount must be greater than zero."));
        if (!Enum.IsDefined(dto.Frequency))
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "Choose how often it repeats."));
        if (dto.Participants.Count == 0 || dto.Payers.Count == 0)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "Choose who paid and who shares it."));

        var activeIds = group.Members.Where(m => m.LeftAt == null).Select(m => m.Id).ToHashSet();
        if (dto.Payers.Any(p => !activeIds.Contains(p.MemberId)) || dto.Participants.Any(p => !activeIds.Contains(p.MemberId)))
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "Everyone must be a current member of the group."));
        if (Math.Abs(dto.Payers.Sum(p => p.AmountPaid) - dto.Amount) > 0.01m)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "The amounts paid must add up to the expense total."));

        var start = DateTime.SpecifyKind(dto.StartDate.Date, DateTimeKind.Utc);
        DateTime? end = dto.EndDate.HasValue ? DateTime.SpecifyKind(dto.EndDate.Value.Date, DateTimeKind.Utc) : null;
        if (end.HasValue && end.Value < start)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.Invalid", "The end date can't be before the start date."));

        var count = await _context.SplitRecurringExpenses.CountAsync(r => r.SplitGroupId == group.Id && r.IsActive);
        if (count >= MaxRecurringPerGroup)
            return Result.Failure<RecurringExpenseDto>(new Error("SplitRecurring.TooMany", $"A group can have up to {MaxRecurringPerGroup} active recurring expenses."));

        var recurring = new SplitRecurringExpense
        {
            SplitGroupId = group.Id,
            CreatedByUserId = userId,
            Description = dto.Description.Trim(),
            Amount = dto.Amount,
            Category = dto.Category,
            SplitType = dto.SplitType,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            PayersJson = JsonSerializer.Serialize(dto.Payers),
            ParticipantsJson = JsonSerializer.Serialize(dto.Participants),
            Frequency = dto.Frequency,
            StartDate = start,
            RunCount = dto.FirstAlreadyAdded ? 1 : 0,
            NextRunDate = DateTime.SpecifyKind(
                SplitRecurrenceHelper.OccurrenceDate(start, dto.Frequency, dto.FirstAlreadyAdded ? 1 : 0), DateTimeKind.Utc),
            EndDate = end,
            LastRunAt = dto.FirstAlreadyAdded ? DateTime.UtcNow : null,
            IsActive = true
        };
        _context.SplitRecurringExpenses.Add(recurring);
        await _context.SaveChangesAsync();

        await AddActivityAsync(group.Id, userId, "RecurringCreated",
            $"set up a {FrequencyWord(dto.Frequency)} expense '{recurring.Description}'.", group);
        return Result.Success(MapRecurring(recurring, group));
    }

    public async Task<Result<List<RecurringExpenseDto>>> GetRecurringAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null || !IsActiveMember(group, userId))
            return Result.Failure<List<RecurringExpenseDto>>(new Error("SplitGroup.NotFound", "Group not found."));

        var items = await _context.SplitRecurringExpenses
            .Where(r => r.SplitGroupId == groupId)
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.NextRunDate)
            .ToListAsync();
        return Result.Success(items.Select(r => MapRecurring(r, group)).ToList());
    }

    public async Task<Result<bool>> SetRecurringActiveAsync(string userId, int recurringId, bool active)
    {
        var (recurring, group, error) = await LoadForManageAsync(userId, recurringId);
        if (error != null) return Result.Failure<bool>(error);

        recurring!.IsActive = active;
        if (active)
        {
            recurring.LastError = null;
            // Resuming never back-fills the time it was paused: skip past occurrences.
            var today = DateTime.UtcNow.Date;
            var n = recurring.RunCount;
            var next = SplitRecurrenceHelper.OccurrenceDate(recurring.StartDate, recurring.Frequency, n);
            while (next < today)
            {
                n++;
                next = SplitRecurrenceHelper.OccurrenceDate(recurring.StartDate, recurring.Frequency, n);
            }
            recurring.RunCount = n;
            recurring.NextRunDate = DateTime.SpecifyKind(next, DateTimeKind.Utc);
        }
        await _context.SaveChangesAsync();
        await AddActivityAsync(group!.Id, userId, "RecurringChanged",
            $"{(active ? "resumed" : "paused")} the recurring expense '{recurring.Description}'.", group);
        return Result.Success(true);
    }

    public async Task<Result<bool>> DeleteRecurringAsync(string userId, int recurringId)
    {
        var (recurring, group, error) = await LoadForManageAsync(userId, recurringId);
        if (error != null) return Result.Failure<bool>(error);

        recurring!.IsDeleted = true;
        recurring.DeletedAt = DateTime.UtcNow;
        recurring.IsActive = false;
        await _context.SaveChangesAsync();
        await AddActivityAsync(group!.Id, userId, "RecurringChanged",
            $"deleted the recurring expense '{recurring.Description}'. Expenses already added stay.", group);
        return Result.Success(true);
    }

    /// <summary>The template's creator or a group admin may pause, resume or delete it.</summary>
    private async Task<(SplitRecurringExpense?, SplitGroup?, Error?)> LoadForManageAsync(string userId, int recurringId)
    {
        var recurring = await _context.SplitRecurringExpenses.FirstOrDefaultAsync(r => r.Id == recurringId);
        if (recurring == null)
            return (null, null, new Error("SplitRecurring.NotFound", "Recurring expense not found."));
        var group = await _context.SplitGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == recurring.SplitGroupId);
        if (group == null || !IsActiveMember(group, userId))
            return (null, null, new Error("SplitRecurring.NotFound", "Recurring expense not found."));
        var isAdmin = group.CreatedByUserId == userId ||
                      group.Members.Any(m => m.LinkedUserId == userId && m.IsAdmin && m.LeftAt == null);
        if (recurring.CreatedByUserId != userId && !isAdmin)
            return (null, null, new Error("SplitGroup.Forbidden", "Only the person who set this up, or a group admin, can change it."));
        if (group.Status == SplitGroupStatus.Archived)
            return (null, null, new Error("SplitGroup.Closed", "This group is closed."));
        return (recurring, group, null);
    }

    public async Task<int> RunDueRecurringAsync(DateTime todayUtc)
    {
        var today = DateTime.SpecifyKind(todayUtc.Date, DateTimeKind.Utc);
        var dueIds = await _context.SplitRecurringExpenses
            .Where(r => r.IsActive && r.NextRunDate <= today)
            .Select(r => r.Id).ToListAsync();

        var added = 0;
        foreach (var id in dueIds)
        {
            try { added += await ProcessOneAsync(id, today); }
            catch (Exception ex) { _logger.LogError(ex, "Recurring split expense {Id} failed.", id); }
        }
        return added;
    }

    private async Task<int> ProcessOneAsync(int id, DateTime today)
    {
        var added = 0;
        for (var i = 0; i < MaxCatchUpPerRun; i++)
        {
            var r = await _context.SplitRecurringExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r == null || !r.IsActive || r.NextRunDate > today) break;

            if (r.EndDate.HasValue && r.NextRunDate > r.EndDate.Value)
            {
                await _context.SplitRecurringExpenses.Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
                break;
            }

            // Claim this occurrence first. If another server got there already, nothing is added twice.
            var occurrence = r.NextRunDate;
            var expectedRuns = r.RunCount;
            var next = DateTime.SpecifyKind(
                SplitRecurrenceHelper.OccurrenceDate(r.StartDate, r.Frequency, expectedRuns + 1), DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            var claimed = await _context.SplitRecurringExpenses
                .Where(x => x.Id == id && x.IsActive && x.RunCount == expectedRuns)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RunCount, expectedRuns + 1)
                    .SetProperty(x => x.NextRunDate, next)
                    .SetProperty(x => x.LastRunAt, now));
            if (claimed == 0) break;

            var result = await _splitService.AddExpenseAsync(r.CreatedByUserId, new CreateExpenseDto
            {
                GroupId = r.SplitGroupId,
                Description = r.Description,
                Amount = r.Amount,
                Date = occurrence,
                Category = r.Category,
                SplitType = r.SplitType,
                Notes = r.Notes ?? "Added automatically (recurring)",
                Payers = JsonSerializer.Deserialize<List<ExpensePayerDto>>(r.PayersJson) ?? new(),
                Participants = JsonSerializer.Deserialize<List<ExpenseParticipantDto>>(r.ParticipantsJson) ?? new()
            });

            if (!result.IsSuccess)
            {
                var message = result.Error.Description;
                await _context.SplitRecurringExpenses.Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.LastError, message));
                break;
            }

            added++;
            await _context.SplitRecurringExpenses.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastError, (string?)null));
        }
        return added;
    }

    // ── Reminders ────────────────────────────────────────────────────────────

    public async Task<Result<ReminderResultDto>> SendReminderAsync(string userId, SendReminderDto dto)
    {
        var group = await LoadGroupWithLedgerAsync(dto.GroupId);
        if (group == null || !IsActiveMember(group, userId))
            return Result.Failure<ReminderResultDto>(new Error("SplitGroup.NotFound", "Group not found."));
        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<ReminderResultDto>(new Error("SplitGroup.Closed", "This group is closed."));

        var me = group.Members.First(m => m.LinkedUserId == userId && m.LeftAt == null);
        var debtor = group.Members.FirstOrDefault(m => m.Id == dto.FromMemberId && m.LeftAt == null);
        if (debtor == null)
            return Result.Failure<ReminderResultDto>(new Error("SplitMember.NotFound", "That member isn't in the group."));

        var debts = SplitBalanceCalculator.BuildPlan(group, SplitBalanceCalculator.CalculateNetBalances(group))
            .Where(d => d.FromMemberId == debtor.Id).ToList();
        if (debts.Count == 0)
            return Result.Failure<ReminderResultDto>(new Error("SplitReminder.NothingOwed", $"{debtor.Name} doesn't owe anything right now."));

        var isAdmin = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId && m.IsAdmin);
        if (!isAdmin && debts.All(d => d.ToMemberId != me.Id))
            return Result.Failure<ReminderResultDto>(new Error("SplitGroup.Forbidden", "You can only remind people who owe you."));

        if (debtor.LinkedUserId == null)
            return Result.Failure<ReminderResultDto>(new Error("SplitReminder.NoAccount",
                $"{debtor.Name} isn't on FinPlanner, so there's no email to send to. Use the WhatsApp reminder instead."));

        var activityMessage = $"reminded {debtor.Name} to pay up.";
        var since = DateTime.UtcNow.AddHours(-24);
        var recently = await _context.SplitActivities.AnyAsync(a =>
            a.SplitGroupId == group.Id && a.Type == "Reminder" && a.Message == activityMessage && a.CreatedAt > since);
        if (recently)
            return Result.Failure<ReminderResultDto>(new Error("SplitReminder.TooSoon", $"{debtor.Name} was already reminded in the last 24 hours."));

        var user = await _users.FindByIdAsync(debtor.LinkedUserId);
        if (string.IsNullOrWhiteSpace(user?.Email))
            return Result.Failure<ReminderResultDto>(new Error("SplitReminder.NoEmail", $"{debtor.Name} has no email address on file."));

        await _email.SendEmailAsync(new MailRequest
        {
            To = user.Email,
            Subject = $"Reminder: you owe money in \"{group.Name}\"",
            Body = BuildReminderEmail(group, debtor.Name, debts, fromName: me.Name)
        });
        await AddActivityAsync(group.Id, userId, "Reminder", activityMessage, group);

        return Result.Success(new ReminderResultDto { SentTo = debtor.Name, Amount = debts.Sum(d => d.Amount) });
    }

    public async Task<int> SendAutoRemindersAsync(DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-7);
        var ids = await _context.SplitGroups
            .Where(g => g.AutoRemind && g.Status != SplitGroupStatus.Archived &&
                        (g.LastAutoRemindAt == null || g.LastAutoRemindAt <= cutoff))
            .Select(g => g.Id).ToListAsync();

        var sent = 0;
        foreach (var id in ids)
        {
            try
            {
                // Claim the week first so two servers don't both send.
                var claimed = await _context.SplitGroups
                    .Where(g => g.Id == id && (g.LastAutoRemindAt == null || g.LastAutoRemindAt <= cutoff))
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastAutoRemindAt, nowUtc));
                if (claimed == 0) continue;

                var group = await LoadGroupWithLedgerAsync(id);
                if (group == null) continue;

                var plan = SplitBalanceCalculator.BuildPlan(group, SplitBalanceCalculator.CalculateNetBalances(group));
                foreach (var byDebtor in plan.GroupBy(d => d.FromMemberId))
                {
                    var debtor = group.Members.FirstOrDefault(m => m.Id == byDebtor.Key && m.LeftAt == null);
                    if (debtor?.LinkedUserId == null) continue;
                    var user = await _users.FindByIdAsync(debtor.LinkedUserId);
                    if (string.IsNullOrWhiteSpace(user?.Email)) continue;

                    await _email.SendEmailAsync(new MailRequest
                    {
                        To = user.Email,
                        Subject = $"Weekly reminder: you owe money in \"{group.Name}\"",
                        Body = BuildReminderEmail(group, debtor.Name, byDebtor.ToList(), fromName: null)
                    });
                    sent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto reminders for split group {Id} failed.", id);
            }
        }
        return sent;
    }

    private string BuildReminderEmail(SplitGroup group, string debtorName, List<SimplifiedDebt> debts, string? fromName)
    {
        static string enc(string? s) => WebUtility.HtmlEncode(s) ?? string.Empty;
        var sb = new StringBuilder();
        sb.Append($"<p>Hi {enc(debtorName)},</p>");
        sb.Append(fromName != null
            ? $"<p>{enc(fromName)} sent you a friendly reminder about <strong>{enc(group.Name)}</strong>:</p>"
            : $"<p>A friendly weekly reminder about <strong>{enc(group.Name)}</strong>:</p>");
        sb.Append("<ul>");
        foreach (var d in debts)
            sb.Append($"<li>You owe <strong>{enc(group.Currency)} {d.Amount:N2}</strong> to {enc(d.ToMemberName)}</li>");
        sb.Append("</ul>");
        var baseUrl = (_config["App:FrontendUrl"] ?? _config["Cors:AllowedOrigins:0"])?.TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
            sb.Append($"<p><a href=\"{enc(baseUrl)}/app/split/{group.Id}\">Open the group to settle up</a></p>");
        sb.Append("<p style=\"color:#888;font-size:12px\">You get this because you are a member of this FinPlanner group.</p>");
        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<SplitGroup?> LoadGroupWithLedgerAsync(int groupId)
        => await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == groupId);

    private static bool IsActiveMember(SplitGroup group, string userId)
        => group.Members.Any(m => m.LinkedUserId == userId && m.LeftAt == null);

    private static string FrequencyWord(SplitRecurrenceFrequency f) => f switch
    {
        SplitRecurrenceFrequency.Daily => "daily",
        SplitRecurrenceFrequency.Weekly => "weekly",
        _ => "monthly"
    };

    private async Task AddActivityAsync(int groupId, string userId, string type, string message, SplitGroup group)
    {
        var actor = group.Members.FirstOrDefault(m => m.LinkedUserId == userId)?.Name ?? "Someone";
        _context.SplitActivities.Add(new SplitActivity
        {
            SplitGroupId = groupId, ActorUserId = userId, ActorName = actor, Type = type, Message = message
        });
        await _context.SaveChangesAsync();
    }

    private static RecurringExpenseDto MapRecurring(SplitRecurringExpense r, SplitGroup group)
    {
        var payers = JsonSerializer.Deserialize<List<ExpensePayerDto>>(r.PayersJson) ?? new();
        var participants = JsonSerializer.Deserialize<List<ExpenseParticipantDto>>(r.ParticipantsJson) ?? new();
        var names = payers.Select(p => group.Members.FirstOrDefault(m => m.Id == p.MemberId)?.Name ?? "Someone").Distinct().ToList();
        return new RecurringExpenseDto
        {
            Id = r.Id,
            GroupId = r.SplitGroupId,
            Description = r.Description,
            Amount = r.Amount,
            Category = r.Category,
            Frequency = r.Frequency,
            NextRunDate = r.NextRunDate,
            EndDate = r.EndDate,
            IsActive = r.IsActive,
            LastRunAt = r.LastRunAt,
            LastError = r.LastError,
            RunCount = r.RunCount,
            PaidBy = names.Count == 1 ? names[0] : $"{names.Count} people",
            ParticipantCount = participants.Count,
            CreatedByUserId = r.CreatedByUserId
        };
    }
}
