using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Investment holdings: manual valuation, gain and XIRR on top of the normal ledger.
/// A contribution is an ordinary transfer from a bank account, so it is never "spending".</summary>
public partial class AccountService
{
    private const int StaleValuationDays = 30;

    private static decimal MonthlyEquivalent(decimal amount, RecurrenceFrequency f) => f switch
    {
        RecurrenceFrequency.Daily => amount * 30m,
        RecurrenceFrequency.Weekly => amount * 52m / 12m,
        RecurrenceFrequency.Monthly => amount,
        RecurrenceFrequency.Yearly => amount / 12m,
        RecurrenceFrequency.Quarterly => amount / 3m,
        RecurrenceFrequency.HalfYearly => amount / 6m,
        _ => 0m // custom / one-time: not counted as a monthly commitment
    };

    public async Task<Result<InvestmentOverviewDto>> GetInvestmentOverviewAsync(string userId)
    {
        var accounts = await _context.Accounts.AsNoTracking()
            .Include(a => a.InvestmentDetails)
            .Where(a => a.UserId == userId && a.AccountCategory.AccountType == AccountType.Investment)
            .ToListAsync();

        var ids = accounts.Select(a => a.Id).ToList();
        var txs = ids.Count == 0 ? new() : await _context.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId) && t.Kind != TransactionKind.BalanceAdjustment)
            .Select(t => new { t.AccountId, t.Date, t.Amount, t.Type, t.Kind })
            .ToListAsync();

        var sipRows = ids.Count == 0 ? new() : await _context.RecurringTransactions.AsNoTracking()
            .Include(r => r.Account)
            .Where(r => r.UserId == userId && r.IsActive && r.LinkedInvestmentAccountId != null && ids.Contains(r.LinkedInvestmentAccountId!.Value))
            .ToListAsync();

        var today = DateTime.UtcNow;
        var dto = new InvestmentOverviewDto();
        var allFlows = new List<(DateTime, decimal)>();

        foreach (var a in accounts.Where(a => !a.IsArchived))
        {
            var mine = txs.Where(t => t.AccountId == a.Id).ToList();
            var net = mine.Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
            var cv = a.InvestmentDetails?.CurrentValue;
            var valuedOn = a.InvestmentDetails?.ValuedOn;
            var value = cv ?? net;
            var gain = cv.HasValue ? cv.Value - net : 0m;

            double? xirr = null;
            if (cv.HasValue)
            {
                // Realised gain/loss entries change net invested but are not cash that moved, so they stay out of XIRR.
                var flows = mine.Where(t => t.Kind != TransactionKind.InvestmentGain)
                    .Select(t => (t.Date, t.Type == TransactionType.Income ? -t.Amount : t.Amount)).ToList();
                var end = valuedOn ?? today;
                if (flows.Count > 0 && end < flows.Max(f => f.Date)) end = flows.Max(f => f.Date);
                flows.Add((end, cv.Value));
                xirr = Xirr.Compute(flows);
                allFlows.AddRange(flows);
            }

            var monthly = sipRows.Where(r => r.LinkedInvestmentAccountId == a.Id).Sum(r => MonthlyEquivalent(r.Amount, r.Frequency));
            dto.Holdings.Add(new InvestmentHoldingDto
            {
                AccountId = a.Id, Name = a.Name,
                Kind = a.InvestmentDetails?.Kind ?? InvestmentKind.Other,
                Symbol = a.InvestmentDetails?.Symbol, Notes = a.InvestmentDetails?.Notes,
                NetInvested = net, CurrentValue = cv, ValuedOn = valuedOn,
                IsStale = cv.HasValue && valuedOn.HasValue && (today - valuedOn.Value).TotalDays > StaleValuationDays,
                Gain = gain,
                GainPercent = cv.HasValue && net > 0 ? Math.Round(gain / net * 100m, 2) : null,
                XirrPercent = xirr.HasValue ? Math.Round(xirr.Value * 100.0, 2) : null,
                MonthlySip = Math.Round(monthly, 2),
                Units = a.InvestmentDetails?.Units, AutoValuation = a.InvestmentDetails?.AutoValuation ?? false,
                LastPrice = a.InvestmentDetails?.LastPrice, LastPriceDate = a.InvestmentDetails?.LastPriceDate,
                LastPriceError = a.InvestmentDetails?.LastPriceError
            });
            dto.TotalNetInvested += net;
            dto.TotalValue += value;
            dto.TotalGain += gain;
        }

        dto.TotalGainPercent = dto.TotalNetInvested > 0 ? Math.Round(dto.TotalGain / dto.TotalNetInvested * 100m, 2) : null;
        // Portfolio XIRR only when every holding is valued (otherwise it would mix cost and value).
        if (dto.Holdings.Count > 0 && dto.Holdings.All(h => h.CurrentValue.HasValue))
        {
            var x = Xirr.Compute(allFlows);
            dto.PortfolioXirrPercent = x.HasValue ? Math.Round(x.Value * 100.0, 2) : null;
        }

        var totalValue = dto.TotalValue;
        dto.Allocation = dto.Holdings.GroupBy(h => h.Kind)
            .Select(g =>
            {
                var v = g.Sum(h => h.CurrentValue ?? h.NetInvested);
                return new AllocationSliceDto { Kind = g.Key, Value = v, Percent = totalValue > 0 ? Math.Round(v / totalValue * 100m, 1) : 0m };
            }).OrderByDescending(s => s.Value).ToList();

        var live = dto.Holdings.ToDictionary(h => h.AccountId);
        dto.Sips = sipRows.Where(r => live.ContainsKey(r.LinkedInvestmentAccountId!.Value)).Select(r => new SipDto
        {
            RecurringId = r.Id, Description = r.Description ?? "", Amount = r.Amount, Frequency = r.Frequency,
            NextDate = r.NextProcessDate, FundingAccountName = r.Account?.Name ?? "",
            InvestmentAccountId = r.LinkedInvestmentAccountId!.Value, InvestmentName = live[r.LinkedInvestmentAccountId!.Value].Name
        }).OrderBy(s => s.NextDate).ToList();
        dto.MonthlySipTotal = dto.Holdings.Sum(h => h.MonthlySip);

        return Result.Success(dto);
    }

    public async Task<Result<bool>> SetInvestmentValuationAsync(string userId, SetInvestmentValuationDto dto)
    {
        if (dto.CurrentValue is < 0)
            return Result.Failure<bool>(new Error("Investment.InvalidValue", "Current value can't be negative."));
        if (dto.Units is < 0)
            return Result.Failure<bool>(new Error("Investment.InvalidUnits", "Units can't be negative."));

        var account = await _context.Accounts
            .Include(a => a.AccountCategory).Include(a => a.InvestmentDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return Result.Failure<bool>(new Error("Account.NotFound", "Investment not found."));
        if (account.AccountCategory?.AccountType != AccountType.Investment)
            return Result.Failure<bool>(new Error("Investment.NotAnInvestment", "This account's category isn't of type Investment."));

        var d = account.InvestmentDetails;
        if (d == null) { d = new InvestmentDetails { AccountId = account.Id }; _context.InvestmentDetails.Add(d); }
        if (dto.Kind.HasValue) d.Kind = dto.Kind.Value;
        if (dto.Symbol != null) d.Symbol = dto.Symbol.Trim().Length == 0 ? null : dto.Symbol.Trim();
        if (dto.Notes != null) d.Notes = dto.Notes.Trim().Length == 0 ? null : dto.Notes.Trim();
        if (dto.Units.HasValue) d.Units = Math.Round(dto.Units.Value, 4);
        if (dto.AutoValuation.HasValue) d.AutoValuation = dto.AutoValuation.Value;

        if (d.AutoValuation && (string.IsNullOrWhiteSpace(d.Symbol) || (d.Units ?? 0) <= 0))
            return Result.Failure<bool>(new Error("Investment.AutoNeedsSymbolUnits",
                "Automatic value needs the scheme code / ticker and the number of units or shares you hold."));

        if (dto.CurrentValue.HasValue)
        {
            var when = (dto.ValuedOn ?? DateTime.UtcNow).EnsureUtc();
            if (when > DateTime.UtcNow.AddDays(1))
                return Result.Failure<bool>(new Error("Investment.FutureValuation", "A valuation can't be dated in the future."));
            d.CurrentValue = Math.Round(dto.CurrentValue.Value, 2);
            d.ValuedOn = when;
        }
        else if (!d.AutoValuation)
            return Result.Failure<bool>(new Error("Investment.ValueRequired", "Enter what it is worth, or turn on automatic value."));

        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return Result.Failure<bool>(save.Error);

        // Fetch the first automatic value straight away so the user sees a number, not an empty row.
        if (d.AutoValuation)
            await _prices.RefreshAsync(userId, account.Id, CancellationToken.None);

        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }

    public async Task<Result<string>> RefreshInvestmentPricesAsync(string userId)
    {
        var r = await _prices.RefreshAsync(userId, null, CancellationToken.None);
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(r.Message);
    }

    public async Task<Result<int>> CreateInvestmentAsync(string userId, CreateInvestmentDto dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return Result.Failure<int>(new Error("Investment.NameRequired", "Give the investment a name."));
        if (dto.InvestedSoFar < 0) return Result.Failure<int>(new Error("Investment.InvalidAmount", "Invested amount can't be negative."));
        if (dto.CurrentValue is < 0) return Result.Failure<int>(new Error("Investment.InvalidValue", "Current value can't be negative."));

        var category = await _context.AccountCategories
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountType == AccountType.Investment);
        if (category == null)
        {
            category = new AccountCategory { Name = "Investments", UserId = userId, IsLiability = false, AccountType = AccountType.Investment };
            _context.AccountCategories.Add(category);
            await _context.SaveChangesAsync(default);
        }

        var created = await UpsertAccountAsync(userId, new UpsertAccountDto
        {
            Name = name, Balance = dto.InvestedSoFar, AccountCategoryId = category.Id, Purpose = "Investment"
        });
        if (created.IsFailure) return Result.Failure<int>(created.Error);

        var details = new InvestmentDetails
        {
            AccountId = created.Value.Id, Kind = dto.Kind,
            CurrentValue = dto.CurrentValue.HasValue ? Math.Round(dto.CurrentValue.Value, 2) : null,
            ValuedOn = dto.CurrentValue.HasValue ? DateTime.UtcNow : null,
            Symbol = string.IsNullOrWhiteSpace(dto.Symbol) ? null : dto.Symbol.Trim(),
            Units = dto.Units is > 0 ? Math.Round(dto.Units.Value, 4) : null,
            AutoValuation = dto.AutoValuation && !string.IsNullOrWhiteSpace(dto.Symbol) && dto.Units is > 0
        };
        _context.InvestmentDetails.Add(details);
        await _context.SaveChangesAsync(default);
        if (details.AutoValuation) await _prices.RefreshAsync(userId, created.Value.Id, CancellationToken.None);
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(created.Value.Id);
    }
}
