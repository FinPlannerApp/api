using Application.Common.Models;
using Application.Contracts;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Integrity.Queries;

public class IntegrityIssueDto
{
    public string Code { get; set; } = string.Empty;
    /// <summary>error | warning | info</summary>
    public string Severity { get; set; } = "warning";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public decimal? Amount { get; set; }
    public List<int> TransactionIds { get; set; } = new();
}

public class LedgerIntegrityReportDto
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int AccountsChecked { get; set; }
    public int TransactionsChecked { get; set; }
    public List<IntegrityIssueDto> Issues { get; set; } = new();
}

public record GetLedgerIntegrityQuery(string UserId) : IRequest<Result<LedgerIntegrityReportDto>>;

/// <summary>
/// Read-only detective control. Stored balances are mutated per transaction, so any bug, crash,
/// manual DB edit or double submit can leave them out of step with the ledger; nothing else
/// notices. This recomputes the invariants and lists what does not hold. It never changes data.
/// </summary>
public class GetLedgerIntegrityQueryHandler : IRequestHandler<GetLedgerIntegrityQuery, Result<LedgerIntegrityReportDto>>
{
    private readonly IApplicationDbContext _context;
    public GetLedgerIntegrityQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<LedgerIntegrityReportDto>> Handle(GetLedgerIntegrityQuery request, CancellationToken ct)
    {
        var userId = request.UserId;
        var report = new LedgerIntegrityReportDto();

        var accounts = await _context.Accounts.AsNoTracking()
            .Include(a => a.AccountCategory)
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
        var accountById = accounts.ToDictionary(a => a.Id);
        var ids = accounts.Select(a => a.Id).ToList();
        report.AccountsChecked = accounts.Count;

        var txs = await _context.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId))
            .Select(t => new { t.Id, t.AccountId, t.Amount, t.Type, t.Date, t.Description, t.Kind, t.TransferGroupId, t.IsBalanceAdjustment })
            .ToListAsync(ct);
        report.TransactionsChecked = txs.Count;

        string Name(int id) => accountById.TryGetValue(id, out var a) ? a.Name : $"#{id}";

        // 1. Stored balance vs ledger
        var ledger = txs.GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount));
        foreach (var a in accounts)
        {
            var computed = ledger.GetValueOrDefault(a.Id);
            var diff = a.Balance - computed;
            if (Math.Abs(diff) >= 0.01m)
                report.Issues.Add(new IntegrityIssueDto
                {
                    Code = "BALANCE_DRIFT", Severity = "error", AccountId = a.Id, AccountName = a.Name, Amount = diff,
                    Title = "Stored balance does not match transactions",
                    Detail = $"{a.Name}: balance is ₹{a.Balance:F2} but its transactions add up to ₹{computed:F2} (difference ₹{diff:F2}). Usually a missing opening-balance entry, an interrupted save or a manual edit."
                });
        }

        // 2. Asset account below zero
        foreach (var a in accounts.Where(a => !a.AccountCategory.IsLiability && a.Balance < 0 && !a.IsArchived))
            report.Issues.Add(new IntegrityIssueDto
            {
                Code = "NEGATIVE_ASSET", Severity = "warning", AccountId = a.Id, AccountName = a.Name, Amount = a.Balance,
                Title = "Asset account is negative",
                Detail = $"{a.Name} shows ₹{a.Balance:F2}. A bank or wallet cannot hold less than zero unless an entry is missing or wrong (or it is an overdraft account)."
            });

        // 3. Broken transfer pairs
        foreach (var g in txs.Where(t => t.Kind == TransactionKind.Transfer && t.TransferGroupId != null).GroupBy(t => t.TransferGroupId))
        {
            var legs = g.ToList();
            var ok = legs.Count == 2 &&
                     legs.Count(l => l.Type == TransactionType.Income) == 1 &&
                     legs[0].Amount == legs[1].Amount;
            if (!ok)
                report.Issues.Add(new IntegrityIssueDto
                {
                    Code = "BROKEN_TRANSFER", Severity = "error", Amount = legs.Sum(l => l.Type == TransactionType.Income ? l.Amount : -l.Amount),
                    TransactionIds = legs.Select(l => l.Id).ToList(),
                    Title = "Transfer does not balance",
                    Detail = $"A transfer has {legs.Count} leg(s) and nets to ₹{legs.Sum(l => l.Type == TransactionType.Income ? l.Amount : -l.Amount):F2}; it should be one debit and one credit of the same amount."
                });
        }

        // 4. Possible duplicates (same account, day, amount, type, description)
        foreach (var g in txs.Where(t => t.TransferGroupId == null)
                     .GroupBy(t => new { t.AccountId, Day = t.Date.Date, t.Amount, t.Type, D = t.Description.Trim().ToLower() })
                     .Where(g => g.Count() > 1))
            report.Issues.Add(new IntegrityIssueDto
            {
                Code = "POSSIBLE_DUPLICATE", Severity = "info", AccountId = g.Key.AccountId, AccountName = Name(g.Key.AccountId),
                Amount = g.Key.Amount, TransactionIds = g.Select(t => t.Id).ToList(),
                Title = "Possible duplicate entries",
                Detail = $"{g.Count()} identical {g.Key.Type} entries of ₹{g.Key.Amount:F2} \"{g.First().Description}\" on {g.Key.Day:dd MMM yyyy} in {Name(g.Key.AccountId)}. Fine if genuinely repeated (e.g. daily SIP)."
            });

        // 5. Future-dated entries
        var horizon = DateTime.UtcNow.AddDays(2);
        var future = txs.Where(t => t.Date > horizon).ToList();
        if (future.Count > 0)
            report.Issues.Add(new IntegrityIssueDto
            {
                Code = "FUTURE_DATED", Severity = "warning", TransactionIds = future.Take(50).Select(t => t.Id).ToList(),
                Title = "Entries dated in the future",
                Detail = $"{future.Count} transaction(s) are dated more than 2 days ahead and already change today's balance."
            });

        // 6. Card bills
        var cardIds = accounts.Where(a => a.AccountCategory.AccountType == AccountType.CreditCard).Select(a => a.Id).ToList();
        var bills = await _context.CreditCardBills.AsNoTracking().Where(b => cardIds.Contains(b.AccountId)).ToListAsync(ct);
        var payments = await _context.CreditCardPayments.AsNoTracking().Where(p => cardIds.Contains(p.CreditCardAccountId)).ToListAsync(ct);
        var liveTxIds = txs.Select(t => t.Id).ToHashSet();

        foreach (var card in cardIds)
        {
            var open = bills.Where(b => b.AccountId == card && !b.IsPaid).OrderBy(b => b.StatementDate).ToList();
            if (open.Count > 1)
                report.Issues.Add(new IntegrityIssueDto
                {
                    Code = "MULTIPLE_OPEN_BILLS", Severity = "warning", AccountId = card, AccountName = Name(card),
                    Amount = open.Sum(b => b.BillAmount),
                    Title = "More than one unpaid statement",
                    Detail = $"{Name(card)} has {open.Count} unpaid statements. Only the latest is counted in Safe-to-Spend, so older unpaid bills can be under-reported. Mark settled ones as paid or record the payments."
                });
        }
        foreach (var b in bills)
        {
            var paid = payments.Where(p => p.CreditCardBillId == b.Id).Sum(p => p.Amount);
            if (paid > b.BillAmount + 0.01m)
                report.Issues.Add(new IntegrityIssueDto
                {
                    Code = "OVERPAID_BILL", Severity = "warning", AccountId = b.AccountId, AccountName = Name(b.AccountId), Amount = paid - b.BillAmount,
                    Title = "Payments exceed the bill",
                    Detail = $"{Name(b.AccountId)} statement {b.StatementDate:dd MMM yyyy}: paid ₹{paid:F2} against a bill of ₹{b.BillAmount:F2}."
                });
        }
        foreach (var p in payments)
        {
            var linked = new[] { p.PrincipalExpenseTransactionId, p.PrincipalIncomeTransactionId }.Where(i => i.HasValue).Select(i => i!.Value).ToList();
            if (linked.Any(i => !liveTxIds.Contains(i)))
                report.Issues.Add(new IntegrityIssueDto
                {
                    Code = "ORPHAN_CARD_PAYMENT", Severity = "error", AccountId = p.CreditCardAccountId, AccountName = Name(p.CreditCardAccountId), Amount = p.Amount,
                    Title = "Card payment points to a missing transaction",
                    Detail = $"A ₹{p.Amount:F2} payment on {Name(p.CreditCardAccountId)} references a transaction that no longer exists, so the bill may show as paid without the money leaving an account."
                });
        }

        report.Issues = report.Issues
            .OrderBy(i => i.Severity == "error" ? 0 : i.Severity == "warning" ? 1 : 2)
            .ToList();
        return Result.Success(report);
    }
}
