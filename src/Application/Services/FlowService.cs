using System.Numerics;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Flow;
using Application.DTOs.Transactions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using AccountType = Domain.Enums.AccountType;

namespace Application.Services;

public interface IFlowService
{
    Task<Result<ObligationsOverviewDto>> GetObligationsAsync(string userId);
    Task<Result<bool>> PauseAsync(string userId, int recurringId);
    Task<Result<bool>> ResumeAsync(string userId, int recurringId);
    Task<Result<bool>> CancelAsync(string userId, int recurringId);
    Task<Result<InvestmentWithdrawPreviewDto>> PreviewWithdrawalAsync(string userId, InvestmentWithdrawDto dto);
    Task<Result<InvestmentWithdrawPreviewDto>> WithdrawAsync(string userId, InvestmentWithdrawDto dto);
}

/// <summary>Obligations (everything that repeats) and the money-out side of investments.</summary>
public class FlowService : IFlowService
{
    private readonly IApplicationDbContext _context;
    private readonly IAccountService _accounts;
    private readonly ITransactionService _transactions;
    private readonly ICacheService _cache;

    public FlowService(IApplicationDbContext context, IAccountService accounts, ITransactionService transactions, ICacheService cache)
    {
        _context = context; _accounts = accounts; _transactions = transactions; _cache = cache;
    }

    // ───────────────────────── obligations ─────────────────────────

    private static int MonthsStep(RecurrenceFrequency f) => f switch
    {
        RecurrenceFrequency.Monthly => 1, RecurrenceFrequency.Quarterly => 3,
        RecurrenceFrequency.HalfYearly => 6, RecurrenceFrequency.Yearly => 12, _ => 0,
    };

    private static string FreqLabel(RecurrenceFrequency f) => f switch
    {
        RecurrenceFrequency.Daily => "Daily", RecurrenceFrequency.Weekly => "Weekly", RecurrenceFrequency.Monthly => "Monthly",
        RecurrenceFrequency.Quarterly => "Quarterly", RecurrenceFrequency.HalfYearly => "Half-yearly", RecurrenceFrequency.Yearly => "Yearly",
        RecurrenceFrequency.Custom => "Custom days", RecurrenceFrequency.OneTime => "One-time", _ => "",
    };

    private static decimal MonthlyEquivalent(RecurringTransaction r) => r.Frequency switch
    {
        RecurrenceFrequency.Daily => r.Amount * 30m,
        RecurrenceFrequency.Weekly => r.Amount * 52m / 12m,
        RecurrenceFrequency.Monthly => r.Amount,
        RecurrenceFrequency.Quarterly => r.Amount / 3m,
        RecurrenceFrequency.HalfYearly => r.Amount / 6m,
        RecurrenceFrequency.Yearly => r.Amount / 12m,
        RecurrenceFrequency.Custom => r.CustomDays.HasValue ? r.Amount * BitOperations.PopCount((uint)(int)r.CustomDays.Value) * 52m / 12m : 0m,
        _ => 0m, // one-time: an upcoming payment, not a monthly commitment
    };

    public async Task<Result<ObligationsOverviewDto>> GetObligationsAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var rts = await _context.RecurringTransactions.AsNoTracking()
            .Include(r => r.Account).Include(r => r.LinkedLoanAccount).Include(r => r.LinkedInvestmentAccount).Include(r => r.Subscription)
            .Where(r => r.UserId == userId && r.Type == TransactionType.Expense && (r.IsActive || r.PausedAt != null))
            .ToListAsync();

        var ids = rts.Select(r => r.Id).ToList();
        var insuranceIds = ids.Count == 0 ? new HashSet<int>()
            : (await _context.InsurancePolicies.Where(p => p.UserId == userId && p.RecurringTransactionId != null && ids.Contains(p.RecurringTransactionId.Value))
                .Select(p => p.RecurringTransactionId!.Value).ToListAsync()).ToHashSet();

        var o = new ObligationsOverviewDto();
        foreach (var r in rts)
        {
            var isInsurance = insuranceIds.Contains(r.Id);
            var group = r.LinkedInvestmentAccountId.HasValue ? "Investing" : r.LinkedLoanAccountId.HasValue ? "Debt" : "Bills";
            var mustPay = group != "Investing" && r.Subscription == null && (r.IsObligation || r.LinkedLoanAccountId.HasValue);
            var paused = r.PausedAt.HasValue && !r.IsActive;
            o.Items.Add(new ObligationDto
            {
                Key = $"rt-{r.Id}", Source = "recurring", RecurringId = r.Id, Name = r.Description ?? "", Group = group,
                Amount = r.Amount, FrequencyLabel = FreqLabel(r.Frequency), NextDate = r.NextProcessDate,
                FromAccount = r.Account?.Name, ToAccount = r.LinkedInvestmentAccount?.Name ?? r.LinkedLoanAccount?.Name,
                MustPay = mustPay, IsPaused = paused, IsInsurance = isInsurance, CanChange = !isInsurance,
                MonthlyEquivalent = paused ? 0m : Math.Round(MonthlyEquivalent(r), 2),
            });
        }

        // Things that are due without a recurring row behind them: card bills and loan EMIs.
        var accountsRes = await _accounts.GetAllAccountsAsync(userId);
        if (accountsRes.IsSuccess)
        {
            var loanCovered = rts.Where(r => r.IsActive && r.LinkedLoanAccountId.HasValue).Select(r => r.LinkedLoanAccountId!.Value).ToHashSet();
            foreach (var a in accountsRes.Value.Where(a => !a.IsArchived))
            {
                var cc = a.CreditCardDetails;
                if (cc?.RemainingBillAmount is > 0 && cc.DueDate.HasValue)
                    o.Items.Add(new ObligationDto
                    {
                        Key = $"card-{a.Id}", Source = "card", Name = $"{a.Name} bill", Group = "Debt", Amount = cc.RemainingBillAmount.Value,
                        FrequencyLabel = "This bill", NextDate = cc.DueDate, MustPay = true, MonthlyEquivalent = 0m,
                    });
                var ln = a.LoanDetails;
                if (ln?.EmiAmount is > 0 && ln.NextEmiDueDate.HasValue && !loanCovered.Contains(a.Id) && a.Balance != 0)
                    o.Items.Add(new ObligationDto
                    {
                        Key = $"loan-{a.Id}", Source = "loan", Name = $"{a.Name} EMI", Group = "Debt", Amount = ln.EmiAmount.Value,
                        FrequencyLabel = "Monthly", NextDate = ln.NextEmiDueDate, MustPay = true, MonthlyEquivalent = ln.EmiAmount.Value,
                    });
            }
        }

        foreach (var i in o.Items.Where(i => !i.IsPaused))
        {
            if (i.Group == "Investing") o.InvestingMonthly += i.MonthlyEquivalent;
            else if (i.MustPay) o.MustPayMonthly += i.MonthlyEquivalent;
            else o.OptionalMonthly += i.MonthlyEquivalent;
        }

        // Calendar: next 60 days of occurrences.
        var horizon = now.AddDays(60);
        foreach (var r in rts.Where(r => r.IsActive && !r.PausedAt.HasValue))
        {
            var item = o.Items.First(i => i.RecurringId == r.Id);
            var d = r.NextProcessDate;
            for (var n = 0; n < 90 && d <= horizon; n++)
            {
                if (r.EndDate.HasValue && d > r.EndDate.Value) break;
                o.Upcoming.Add(new ObligationEventDto { Date = d, Name = r.Description ?? "", Group = item.Group, Amount = r.Amount, MustPay = item.MustPay });
                var step = MonthsStep(r.Frequency);
                d = r.Frequency switch
                {
                    RecurrenceFrequency.Daily => d.AddDays(1),
                    RecurrenceFrequency.Weekly => d.AddDays(7),
                    _ when step > 0 => RecurrenceCalculator.NextEveryMonthsAfter(r.StartDate, d, step),
                    _ => DateTime.MaxValue, // custom / one-time: only the next one is shown
                };
                if (d == DateTime.MaxValue) break;
            }
        }
        foreach (var i in o.Items.Where(i => i.Source != "recurring" && i.NextDate.HasValue && i.NextDate.Value <= horizon))
            o.Upcoming.Add(new ObligationEventDto { Date = i.NextDate!.Value, Name = i.Name, Group = i.Group, Amount = i.Amount, MustPay = i.MustPay });

        o.Upcoming = o.Upcoming.OrderBy(e => e.Date).Take(300).ToList();
        o.Next30DaysOutflow = o.Upcoming.Where(e => e.Date <= now.AddDays(30)).Sum(e => e.Amount);
        o.Items = o.Items.OrderBy(i => i.IsPaused).ThenBy(i => i.Group).ThenBy(i => i.NextDate ?? DateTime.MaxValue).ToList();
        o.MustPayMonthly = Math.Round(o.MustPayMonthly, 2); o.InvestingMonthly = Math.Round(o.InvestingMonthly, 2); o.OptionalMonthly = Math.Round(o.OptionalMonthly, 2);
        return Result.Success(o);
    }

    private async Task<Result<RecurringTransaction>> LoadForChangeAsync(string userId, int id)
    {
        var rt = await _context.RecurringTransactions.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (rt == null) return Result.Failure<RecurringTransaction>(new Error("Obligation.NotFound", "That payment was not found."));
        if (await _context.InsurancePolicies.AnyAsync(p => p.UserId == userId && p.RecurringTransactionId == id))
            return Result.Failure<RecurringTransaction>(new Error("Obligation.Insurance", "This premium belongs to an insurance policy. Change it from the Insurance page."));
        return Result.Success(rt);
    }

    public async Task<Result<bool>> PauseAsync(string userId, int recurringId)
    {
        var l = await LoadForChangeAsync(userId, recurringId);
        if (l.IsFailure) return Result.Failure<bool>(l.Error);
        var rt = l.Value;
        if (!rt.IsActive) return Result.Failure<bool>(new Error("Obligation.NotActive", "It is not running, so there is nothing to pause."));
        rt.IsActive = false; rt.PausedAt = DateTime.UtcNow;
        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return save;
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }

    public async Task<Result<bool>> ResumeAsync(string userId, int recurringId)
    {
        var l = await LoadForChangeAsync(userId, recurringId);
        if (l.IsFailure) return Result.Failure<bool>(l.Error);
        var rt = l.Value;
        if (!rt.PausedAt.HasValue) return Result.Failure<bool>(new Error("Obligation.NotPaused", "It is not paused."));

        // Never leave a past date behind: the daily job would back-post every month that was paused.
        var next = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(rt.StartDate, rt.Frequency, rt.CustomDays, DateTime.UtcNow);
        if (next < DateTime.UtcNow) next = DateTime.UtcNow;
        if (rt.EndDate.HasValue && next > rt.EndDate.Value)
            return Result.Failure<bool>(new Error("Obligation.Ended", "Its end date has passed, so it cannot be resumed."));

        rt.NextProcessDate = next; rt.IsActive = true; rt.PausedAt = null;
        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return save;
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }

    public async Task<Result<bool>> CancelAsync(string userId, int recurringId)
    {
        var l = await LoadForChangeAsync(userId, recurringId);
        if (l.IsFailure) return Result.Failure<bool>(l.Error);
        var rt = l.Value;
        rt.IsActive = false; rt.PausedAt = null;
        if (!rt.EndDate.HasValue || rt.EndDate.Value > DateTime.UtcNow) rt.EndDate = DateTime.UtcNow;
        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return save;
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }

    // ───────────────────────── investment withdrawal ─────────────────────────

    private sealed record Plan(Account Inv, InvestmentDetails? Details, Account Dest, InvestmentWithdrawPreviewDto Calc, DateTime DateUtc, bool Full);

    private async Task<Result<Plan>> PlanAsync(string userId, InvestmentWithdrawDto dto)
    {
        if (dto.ReceivedAmount <= 0) return Result.Failure<Plan>(new Error("Withdraw.Amount", "Enter the amount you received."));
        var gross = dto.GrossAmount ?? dto.ReceivedAmount;
        if (gross < dto.ReceivedAmount)
            return Result.Failure<Plan>(new Error("Withdraw.Gross", "The amount before deductions can't be less than what you received."));
        if (gross > 100_000_000_000m) return Result.Failure<Plan>(new Error("Withdraw.Amount", "That amount is not realistic."));

        var dateUtc = dto.Date.EnsureUtc();
        if (dateUtc > DateTime.UtcNow.AddDays(1))
            return Result.Failure<Plan>(new Error("Withdraw.Date", "A withdrawal can't be dated in the future."));

        var inv = await _context.Accounts.Include(a => a.AccountCategory).Include(a => a.InvestmentDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (inv == null || inv.AccountCategory?.AccountType != AccountType.Investment)
            return Result.Failure<Plan>(new Error("Withdraw.NotInvestment", "Investment not found."));

        var dest = await _context.Accounts.Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == dto.DestinationAccountId && a.UserId == userId);
        var dt = dest?.AccountCategory?.AccountType;
        if (dest == null || dest.Id == inv.Id || dt is AccountType.Loan or AccountType.Investment or AccountType.CreditCard)
            return Result.Failure<Plan>(new Error("Withdraw.Destination", "Choose the bank or cash account the money was credited to."));

        var net = await _context.Transactions
            .Where(t => t.AccountId == inv.Id && t.Kind != TransactionKind.BalanceAdjustment)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        var details = inv.InvestmentDetails;
        var cv = details?.CurrentValue;
        var value = cv ?? net;
        if (value <= 0) return Result.Failure<Plan>(new Error("Withdraw.NoValue", "This investment has no value recorded. Update its value first."));
        if (gross > value + 1m)
            return Result.Failure<Plan>(new Error("Withdraw.TooMuch", $"That is more than the holding is worth (₹{value:N2}). Update its value first if it has grown."));

        var full = gross >= value - 0.5m;
        var cost = full ? Math.Max(net, 0m) : Math.Round(Math.Max(net, 0m) * gross / value, 2);
        var calc = new InvestmentWithdrawPreviewDto
        {
            Gross = Math.Round(gross, 2), Charges = Math.Round(gross - dto.ReceivedAmount, 2), CostRedeemed = cost,
            Gain = Math.Round(gross - cost, 2), ValueBefore = Math.Round(value, 2), ValueAfter = Math.Round(Math.Max(value - gross, 0m), 2),
            Estimated = cv == null || dto.GrossAmount == null,
        };
        var notes = new List<string>();
        if (cv == null) notes.Add("No value is recorded for this investment, so the gain is assumed to be zero.");
        if (dto.GrossAmount == null) notes.Add("No amount-before-deductions was given, so no tax or charges are recorded. If tax was withheld, enter the gross amount.");
        calc.Warning = notes.Count == 0 ? null : string.Join(" ", notes);
        return Result.Success(new Plan(inv, details, dest!, calc, dateUtc, full));
    }

    public async Task<Result<InvestmentWithdrawPreviewDto>> PreviewWithdrawalAsync(string userId, InvestmentWithdrawDto dto)
    {
        var p = await PlanAsync(userId, dto);
        return p.IsFailure ? Result.Failure<InvestmentWithdrawPreviewDto>(p.Error) : Result.Success(p.Value.Calc);
    }

    private async Task<int> EnsureCategoryAsync(string userId, string name)
    {
        var c = await _context.TransactionCategories.FirstOrDefaultAsync(x => x.UserId == userId && x.Name == name && x.ParentCategoryId == null);
        if (c != null) return c.Id;
        c = new TransactionCategory { Name = name, UserId = userId };
        _context.TransactionCategories.Add(c);
        await _context.SaveChangesAsync(default);
        return c.Id;
    }

    public async Task<Result<InvestmentWithdrawPreviewDto>> WithdrawAsync(string userId, InvestmentWithdrawDto dto)
    {
        var planRes = await PlanAsync(userId, dto);
        if (planRes.IsFailure) return Result.Failure<InvestmentWithdrawPreviewDto>(planRes.Error);
        var plan = planRes.Value; var c = plan.Calc;

        await using var dbTx = await ((DbContext)_context).Database.BeginTransactionAsync();
        try
        {
            var gainCat = await EnsureCategoryAsync(userId, "Investment gains");
            var chargeCat = await EnsureCategoryAsync(userId, "Tax & charges");

            // 1) Realised gain/loss on the investment itself, so its net invested falls by the cost redeemed only.
            if (c.Gain != 0m)
            {
                _context.Transactions.Add(new Transaction
                {
                    UserId = userId, AccountId = plan.Inv.Id,
                    Description = c.Gain > 0 ? $"Realised gain on withdrawal: {plan.Inv.Name}" : $"Realised loss on withdrawal: {plan.Inv.Name}",
                    Amount = Math.Abs(c.Gain), Date = plan.DateUtc,
                    Type = c.Gain > 0 ? TransactionType.Income : TransactionType.Expense,
                    Kind = TransactionKind.InvestmentGain, TransactionCategoryId = gainCat,
                    // A loss is a change in the investment's value, not money you spent: a group id keeps it out of
                    // every spending total (same mechanism balance adjustments use). The row still lists and deletes normally.
                    TransferGroupId = c.Gain < 0 ? Guid.NewGuid() : null,
                });
                plan.Inv.Balance += c.Gain;
                _context.Accounts.Update(plan.Inv);
                var s1 = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
                if (s1.IsFailure) { await dbTx.RollbackAsync(); return Result.Failure<InvestmentWithdrawPreviewDto>(s1.Error); }
            }

            // 2) The money moves: investment -> bank (a transfer, not income).
            var t = await _transactions.CreateTransferAsync(userId, plan.Inv.Id, new CreateTransferDto
            {
                Description = string.IsNullOrWhiteSpace(dto.Notes) ? $"Withdrawal from {plan.Inv.Name}" : $"Withdrawal from {plan.Inv.Name}: {dto.Notes.Trim()}",
                Amount = c.Gross, Date = plan.DateUtc, DestinationAccountId = plan.Dest.Id,
            });
            if (t.IsFailure) { await dbTx.RollbackAsync(); return Result.Failure<InvestmentWithdrawPreviewDto>(t.Error); }

            // 3) What never reached the bank: tax, exit load, charges.
            if (c.Charges > 0m)
            {
                var ch = await _transactions.UpsertTransactionAsync(userId, plan.Dest.Id, new UpsertTransactionDto
                {
                    Description = $"Tax & charges: {plan.Inv.Name} withdrawal", Amount = c.Charges, Type = TransactionType.Expense,
                    Date = plan.DateUtc, TransactionCategoryId = chargeCat,
                });
                if (ch.IsFailure) { await dbTx.RollbackAsync(); return Result.Failure<InvestmentWithdrawPreviewDto>(ch.Error); }
            }

            // 4) Value / units fall by what was taken out.
            var d = plan.Details;
            if (d != null)
            {
                if (d.AutoValuation && d.Units is > 0)
                    d.Units = plan.Full ? 0m : Math.Round(d.Units.Value * (1m - c.Gross / c.ValueBefore), 4);
                else if (d.CurrentValue.HasValue)
                {
                    d.CurrentValue = c.ValueAfter; d.ValuedOn = DateTime.UtcNow;
                }
            }
            var s4 = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
            if (s4.IsFailure) { await dbTx.RollbackAsync(); return Result.Failure<InvestmentWithdrawPreviewDto>(s4.Error); }
            await dbTx.CommitAsync();
        }
        catch
        {
            await dbTx.RollbackAsync();
            throw;
        }

        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(c);
    }
}
