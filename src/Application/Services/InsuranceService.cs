using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Insurance;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using AccountType = Domain.Enums.AccountType;

namespace Application.Services;

public interface IInsuranceService
{
    Task<Result<InsuranceOverviewDto>> GetOverviewAsync(string userId);
    Task<Result<InsurancePolicyDto>> UpsertAsync(string userId, UpsertInsurancePolicyDto dto);
    Task<Result<InsurancePolicyDto>> MarkPaidAsync(string userId, int id);
    Task<Result<bool>> DeleteAsync(string userId, int id);
}

public class InsuranceService : IInsuranceService
{
    private readonly IApplicationDbContext _context;
    public InsuranceService(IApplicationDbContext context) => _context = context;

    private const decimal MaxMoney = 100_000_000_000m; // sanity ceiling, avoids numeric overflow

    private static int PaymentsPerYear(int freq) => (InsuranceFrequency)freq switch
    {
        InsuranceFrequency.Monthly => 12,
        InsuranceFrequency.Quarterly => 4,
        InsuranceFrequency.HalfYearly => 2,
        InsuranceFrequency.Yearly => 1,
        _ => 0,
    };

    private static DateTime Advance(DateTime d, int freq) => (InsuranceFrequency)freq switch
    {
        InsuranceFrequency.Monthly => d.AddMonths(1),
        InsuranceFrequency.Quarterly => d.AddMonths(3),
        InsuranceFrequency.HalfYearly => d.AddMonths(6),
        InsuranceFrequency.Yearly => d.AddYears(1),
        _ => d,
    };

    private static DateTime? DateOnlyOrNull(DateTime? d) => d?.Date;

    private static InsurancePolicyDto Map(InsurancePolicy p, DateTime today, RecurringTransaction? rt = null,
        string? payAccountName = null, (DateTime? Last, decimal Total, int Count)? paid = null)
    {
        // A linked obligation owns the schedule: its next date moves as premiums get posted.
        DateTime? nextDue = rt is { IsActive: true } && !rt.IsDeleted ? rt.NextProcessDate.ToLocal().Date : p.NextDueDate;
        var due = nextDue?.Date;
        var exp = p.ExpiryDate?.Date;
        return new InsurancePolicyDto
        {
            Id = p.Id, Kind = p.Kind, Name = p.Name, Insurer = p.Insurer, PolicyNumber = p.PolicyNumber,
            InsuredPeople = p.InsuredPeople, Nominee = p.Nominee, SumAssured = p.SumAssured, Premium = p.Premium,
            PremiumFrequency = p.PremiumFrequency, StartDate = p.StartDate, NextDueDate = nextDue,
            ExpiryDate = p.ExpiryDate, WaitingPeriodEnds = p.WaitingPeriodEnds, Notes = p.Notes, IsActive = p.IsActive,
            PayFromAccountId = p.PayFromAccountId, PayFromAccountName = payAccountName, AutoRecord = p.AutoRecord && p.RecurringTransactionId.HasValue,
            LastPaidOn = paid?.Last, TotalPaid = paid?.Total ?? 0, PaymentsCount = paid?.Count ?? 0,
            AnnualPremium = p.Premium * PaymentsPerYear(p.PremiumFrequency),
            DaysToDue = due.HasValue ? (int)(due.Value - today).TotalDays : null,
            DaysToExpiry = exp.HasValue ? (int)(exp.Value - today).TotalDays : null,
            InWaitingPeriod = p.WaitingPeriodEnds.HasValue && p.WaitingPeriodEnds.Value.Date > today,
        };
    }

    public async Task<Result<InsuranceOverviewDto>> GetOverviewAsync(string userId)
    {
        var today = AppTimeZone.TodayLocal();
        var rows = await _context.InsurancePolicies.Where(p => p.UserId == userId).OrderBy(p => p.Name).ToListAsync();
        var rtIds = rows.Where(r => r.RecurringTransactionId.HasValue).Select(r => r.RecurringTransactionId!.Value).ToList();
        var rts = rtIds.Count == 0 ? new Dictionary<int, RecurringTransaction>()
            : await _context.RecurringTransactions.Where(r => rtIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var accIds = rows.Where(r => r.PayFromAccountId.HasValue).Select(r => r.PayFromAccountId!.Value).Distinct().ToList();
        var accNames = accIds.Count == 0 ? new Dictionary<int, string>()
            : await _context.Accounts.Where(a => accIds.Contains(a.Id) && a.UserId == userId).ToDictionaryAsync(a => a.Id, a => a.Name);
        var paidRows = rtIds.Count == 0 ? new List<(int RtId, DateTime Last, decimal Total, int Count)>()
            : (await _context.Transactions.Where(t => t.RecurringTransactionId != null && rtIds.Contains(t.RecurringTransactionId.Value))
                .GroupBy(t => t.RecurringTransactionId!.Value)
                .Select(g => new { RtId = g.Key, Last = g.Max(t => t.Date), Total = g.Sum(t => t.Amount), Count = g.Count() })
                .ToListAsync()).Select(x => (x.RtId, x.Last, x.Total, x.Count)).ToList();
        var paidMap = paidRows.ToDictionary(x => x.RtId, x => ((DateTime?)x.Last, x.Total, x.Count));

        var dtos = rows.Select(p => Map(p, today,
                p.RecurringTransactionId.HasValue && rts.TryGetValue(p.RecurringTransactionId.Value, out var rt) ? rt : null,
                p.PayFromAccountId.HasValue && accNames.TryGetValue(p.PayFromAccountId.Value, out var an) ? an : null,
                p.RecurringTransactionId.HasValue && paidMap.TryGetValue(p.RecurringTransactionId.Value, out var pd) ? pd : null))
            .OrderBy(d => d.NextDueDate ?? DateTime.MaxValue).ThenBy(d => d.Name).ToList();
        var active = dtos.Where(d => d.IsActive && (d.DaysToExpiry == null || d.DaysToExpiry >= 0)).ToList();

        var overview = new InsuranceOverviewDto
        {
            Policies = dtos,
            TotalAnnualPremium = active.Sum(d => d.AnnualPremium),
            // Payments falling due (or overdue) within the next 30 days; one payment each, not repeated.
            NextThirtyDaysPremium = active.Where(d => d.DaysToDue.HasValue && d.DaysToDue.Value <= 30).Sum(d => d.Premium),
            ByKind = active.GroupBy(d => d.Kind).Select(g => new InsuranceCoverByKindDto
            {
                Kind = g.Key, Policies = g.Count(), SumAssured = g.Sum(x => x.SumAssured), AnnualPremium = g.Sum(x => x.AnnualPremium),
            }).OrderBy(x => x.Kind).ToList(),
        };
        return Result.Success(overview);
    }


    private static RecurrenceFrequency? ToRecurrence(int freq) => (InsuranceFrequency)freq switch
    {
        InsuranceFrequency.Monthly => RecurrenceFrequency.Monthly,
        InsuranceFrequency.Quarterly => RecurrenceFrequency.Quarterly,
        InsuranceFrequency.HalfYearly => RecurrenceFrequency.HalfYearly,
        InsuranceFrequency.Yearly => RecurrenceFrequency.Yearly,
        _ => null,
    };

    private async Task<int> EnsureInsuranceCategoryAsync(string userId)
    {
        var cat = await _context.TransactionCategories
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == "Insurance" && c.ParentCategoryId == null);
        if (cat != null) return cat.Id;
        cat = new TransactionCategory { Name = "Insurance", UserId = userId };
        _context.TransactionCategories.Add(cat);
        await _context.SaveChangesAsync(default);
        return cat.Id;
    }

    /// <summary>Turns the recurring obligation off (the premium history stays where it is).</summary>
    private async Task StopObligationAsync(InsurancePolicy p, bool delete)
    {
        if (!p.RecurringTransactionId.HasValue) return;
        var rt = await _context.RecurringTransactions.FirstOrDefaultAsync(r => r.Id == p.RecurringTransactionId.Value && r.UserId == p.UserId);
        if (rt != null)
        {
            // Carry the schedule back so the policy still shows its next due date.
            p.NextDueDate ??= rt.NextProcessDate.ToLocal().Date;
            rt.IsActive = false;
            if (delete) { rt.IsDeleted = true; rt.DeletedAt = DateTime.UtcNow; }
        }
        p.RecurringTransactionId = null;
    }

    private async Task<Result<bool>> SyncObligationAsync(string userId, InsurancePolicy p, DateTime? previousNextDue, int? previousFreq)
    {
        var wants = p.AutoRecord && p.PayFromAccountId.HasValue && p.IsActive && p.Premium > 0
                    && ToRecurrence(p.PremiumFrequency).HasValue && p.NextDueDate.HasValue;
        if (!wants) { await StopObligationAsync(p, delete: false); return Result.Success(true); }

        var freq = ToRecurrence(p.PremiumFrequency)!.Value;
        var next = p.NextDueDate!.Value.Date.ToUtc();
        DateTime? end = p.ExpiryDate.HasValue ? p.ExpiryDate.Value.Date.AddDays(1).ToUtc().AddTicks(-1) : null;
        var catId = await EnsureInsuranceCategoryAsync(userId);
        var description = $"Insurance premium: {p.Name}";

        RecurringTransaction? rt = p.RecurringTransactionId.HasValue
            ? await _context.RecurringTransactions.FirstOrDefaultAsync(r => r.Id == p.RecurringTransactionId.Value && r.UserId == userId)
            : null;

        if (rt == null)
        {
            rt = new RecurringTransaction
            {
                UserId = userId, AccountId = p.PayFromAccountId!.Value, Description = description, Amount = p.Premium,
                Type = TransactionType.Expense, Frequency = freq, StartDate = next, NextProcessDate = next, EndDate = end,
                TransactionCategoryId = catId, IsObligation = true, IsActive = true,
            };
            _context.RecurringTransactions.Add(rt);
        }
        else
        {
            rt.AccountId = p.PayFromAccountId!.Value; rt.Description = description; rt.Amount = p.Premium; rt.Frequency = freq;
            rt.EndDate = end; rt.TransactionCategoryId = catId; rt.IsObligation = true; rt.IsActive = true;
            // The form shows the obligation's own next date, so a different date here means the user moved it.
            var currentNext = rt.NextProcessDate.ToLocal().Date;
            if (currentNext != p.NextDueDate!.Value.Date || previousFreq != p.PremiumFrequency)
            {
                rt.StartDate = next; rt.NextProcessDate = next;
            }
        }
        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return save;
        p.RecurringTransactionId = rt.Id;
        return Result.Success(true);
    }

    public async Task<Result<InsurancePolicyDto>> UpsertAsync(string userId, UpsertInsurancePolicyDto dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > 120)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.InvalidName", "Give the policy a name (up to 120 characters)."));
        if (!Enum.IsDefined(typeof(InsuranceKind), dto.Kind))
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.InvalidKind", "Unknown policy type."));
        if (!Enum.IsDefined(typeof(InsuranceFrequency), dto.PremiumFrequency))
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.InvalidFrequency", "Unknown premium frequency."));
        if (dto.SumAssured < 0 || dto.SumAssured > MaxMoney || dto.Premium < 0 || dto.Premium > MaxMoney)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.InvalidAmount", "Cover and premium must be between 0 and a realistic amount."));
        if (dto.StartDate.HasValue && dto.ExpiryDate.HasValue && dto.ExpiryDate.Value.Date < dto.StartDate.Value.Date)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.InvalidDates", "The end date is before the start date."));

        if (dto.PayFromAccountId.HasValue)
        {
            var acc = await _context.Accounts.Include(a => a.AccountCategory)
                .FirstOrDefaultAsync(a => a.Id == dto.PayFromAccountId.Value && a.UserId == userId);
            if (acc == null)
                return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AccountNotFound", "The account to pay from was not found."));
            var t = acc.AccountCategory.AccountType;
            if (t == AccountType.Loan || t == AccountType.Investment)
                return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AccountType", "Pay premiums from a bank, cash or credit-card account."));
        }
        if (dto.AutoRecord && !dto.PayFromAccountId.HasValue)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AutoNeedsAccount", "Choose the account the premium is paid from to record it automatically."));
        if (dto.AutoRecord && dto.PremiumFrequency == (int)InsuranceFrequency.Single)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AutoNeedsSchedule", "A one-time premium has no schedule to record automatically."));
        if (dto.AutoRecord && (dto.Premium <= 0 || !dto.NextDueDate.HasValue))
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AutoNeedsDue", "Automatic recording needs the premium amount and the next due date."));

        static string? Clip(string? s, int max) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null : (s.Length > max ? s[..max] : s); }

        InsurancePolicy p;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.InsurancePolicies.FirstOrDefaultAsync(x => x.Id == dto.Id.Value && x.UserId == userId);
            if (existing == null) return Result.Failure<InsurancePolicyDto>(new Error("Insurance.NotFound", "Policy not found."));
            p = existing;
        }
        else
        {
            p = new InsurancePolicy { UserId = userId, Name = name };
            _context.InsurancePolicies.Add(p);
        }

        var prevFreq = p.Id == 0 ? (int?)null : p.PremiumFrequency;
        p.Name = name; p.Kind = dto.Kind; p.Insurer = Clip(dto.Insurer, 120); p.PolicyNumber = Clip(dto.PolicyNumber, 60);
        p.InsuredPeople = Clip(dto.InsuredPeople, 200); p.Nominee = Clip(dto.Nominee, 120); p.Notes = Clip(dto.Notes, 1000);
        p.SumAssured = dto.SumAssured; p.Premium = dto.Premium; p.PremiumFrequency = dto.PremiumFrequency;
        p.StartDate = DateOnlyOrNull(dto.StartDate); p.ExpiryDate = DateOnlyOrNull(dto.ExpiryDate);
        p.WaitingPeriodEnds = DateOnlyOrNull(dto.WaitingPeriodEnds);
        // A one-off premium has no "next due".
        p.NextDueDate = dto.PremiumFrequency == (int)InsuranceFrequency.Single ? null : DateOnlyOrNull(dto.NextDueDate);
        p.IsActive = dto.IsActive;
        p.PayFromAccountId = dto.PayFromAccountId; p.AutoRecord = dto.AutoRecord;

        var sync = await SyncObligationAsync(userId, p, null, prevFreq);
        if (sync.IsFailure) return Result.Failure<InsurancePolicyDto>(sync.Error);

        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (!save.IsSuccess) return Result.Failure<InsurancePolicyDto>(save.Error);
        return Result.Success(Map(p, AppTimeZone.TodayLocal()));
    }

    /// <summary>Premium paid: move the due date forward one period. Does not create a transaction.</summary>
    public async Task<Result<InsurancePolicyDto>> MarkPaidAsync(string userId, int id)
    {
        var p = await _context.InsurancePolicies.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (p == null) return Result.Failure<InsurancePolicyDto>(new Error("Insurance.NotFound", "Policy not found."));
        if (p.RecurringTransactionId.HasValue)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.AutoRecorded",
                "This premium is recorded automatically from the linked account on its due date."));
        if (p.NextDueDate == null)
            return Result.Failure<InsurancePolicyDto>(new Error("Insurance.NoDueDate", "This policy has no upcoming premium date."));

        var next = Advance(p.NextDueDate.Value.Date, p.PremiumFrequency);
        p.NextDueDate = (p.ExpiryDate.HasValue && next > p.ExpiryDate.Value.Date) ? null : next;

        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (!save.IsSuccess) return Result.Failure<InsurancePolicyDto>(save.Error);
        return Result.Success(Map(p, AppTimeZone.TodayLocal()));
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var p = await _context.InsurancePolicies.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (p == null) return Result.Failure<bool>(new Error("Insurance.NotFound", "Policy not found."));
        await StopObligationAsync(p, delete: true);
        p.IsDeleted = true; p.DeletedAt = DateTime.UtcNow;
        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        return save.IsSuccess ? Result.Success(true) : Result.Failure<bool>(save.Error);
    }
}
