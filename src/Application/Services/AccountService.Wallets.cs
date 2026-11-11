using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Cashback, payment-app wallets and payment suggestions.</summary>
public partial class AccountService
{
    public async Task<Result<CashbackInsightsDto>> GetCashbackInsightsAsync(
        string userId, int? accountId = null, int? month = null, int? year = null)
    {
        var query = _context.CreditCardPayments
            .Where(p => p.UserId == userId);

        if (accountId.HasValue)
        {
            query = query.Where(p => p.CreditCardAccountId == accountId.Value);
        }

        if (month.HasValue && year.HasValue)
        {
            var monthStart = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            query = query.Where(p => p.Date >= monthStart && p.Date <= monthEnd);
        }

        var payments = await query.ToListAsync();

        var byApp = payments
            .Where(p => p.PaymentAppName != null)
            .GroupBy(p => p.PaymentAppName!)
            .Select(g =>
            {
                var totalPaid = g.Sum(p => p.Amount);
                var totalCashback = g.Sum(p => p.CashbackAmount ?? 0);
                return new PaymentAppCashbackDto
                {
                    PaymentAppName = g.Key,
                    TotalPaid = totalPaid,
                    TotalCashback = totalCashback,
                    EffectiveRatePercent = totalPaid > 0 ? Math.Round((totalCashback / totalPaid) * 100, 2) : 0,
                    PaymentCount = g.Count()
                };
            })
            .OrderByDescending(a => a.EffectiveRatePercent)
            .ToList();

        var unclaimedWalletCashback = payments
            .Where(p => p.CashbackType == CashbackType.Indirect)
            .Sum(p => p.CashbackAmount ?? 0);

        return Result.Success(new CashbackInsightsDto
        {
            ByApp = byApp,
            UnclaimedWalletCashback = unclaimedWalletCashback
        });
    }

    public async Task<Result<List<AccountPaymentSuggestionDto>>> GetPaymentSuggestionsAsync(
        string userId, int creditCardAccountId, decimal amount)
    {
        var ccAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == creditCardAccountId);

        if (ccAccount == null || ccAccount.UserId != userId)
            return Result.Failure<List<AccountPaymentSuggestionDto>>(new Error(
                "Account.NotFound", "Credit card account not found."));

        var candidateAccounts = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Where(a => a.UserId == userId && a.Id != creditCardAccountId &&
                        a.AccountCategory.AccountType != AccountType.CreditCard)
            .ToListAsync();

        var suggestions = candidateAccounts
            .Select(a => new AccountPaymentSuggestionDto
            {
                AccountId = a.Id,
                AccountName = a.Name,
                Balance = a.Balance,
                HasSufficientBalance = a.Balance >= amount,
                Shortfall = Math.Max(0, amount - a.Balance)
            })
            .OrderByDescending(s => s.Balance)
            .ToList();

        return Result.Success(suggestions);
    }

    public async Task<Result<List<string>>> GetKnownPaymentAppNamesAsync(string userId)
    {
        var names = await _context.CreditCardPayments
            .Where(p => p.UserId == userId && p.PaymentAppName != null)
            .Select(p => p.PaymentAppName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        return Result.Success(names);
    }

    public async Task<Result<List<PaymentAppWalletDto>>> GetPaymentAppWalletsAsync(string userId)
    {
        var entries = await _context.PaymentAppWalletLedgerEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Date)
            .ToListAsync();

        var result = entries
            .GroupBy(e => e.PaymentAppName)
            .Select(g => new PaymentAppWalletDto
            {
                PaymentAppName = g.Key,
                CurrentBalance = g.Sum(e => e.Type == PaymentAppWalletEntryType.Earned ? e.Amount : -e.Amount),
                RecentEntries = g.Take(20).Select(e => new PaymentAppWalletLedgerEntryDto
                {
                    Amount = e.Amount,
                    Type = e.Type.ToString(),
                    Date = e.Date,
                    CreditCardPaymentId = e.CreditCardPaymentId
                }).ToList()
            })
            .OrderByDescending(w => w.CurrentBalance)
            .ToList();

        return Result.Success(result);
    }
}
