using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Transactions.Commands;

public record ValidateBulkTransactionsCommand(string UserId, BulkTransactionPayloadDto Payload)
    : IRequest<Result<BulkValidationResultDto>>;

/// <summary>
/// Dry run of a bulk add: nothing is written. Replays the rows in date order against each account's
/// current balance (exactly how the real save will apply them) and flags rows that would fail, plus
/// duplicates inside the batch and against existing entries.
/// </summary>
public class ValidateBulkTransactionsCommandHandler
    : IRequestHandler<ValidateBulkTransactionsCommand, Result<BulkValidationResultDto>>
{
    public const int MaxRows = 500;
    private readonly IApplicationDbContext _context;
    public ValidateBulkTransactionsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<BulkValidationResultDto>> Handle(ValidateBulkTransactionsCommand request, CancellationToken ct)
    {
        var rows = request.Payload.Transactions;
        var result = new BulkValidationResultDto();
        if (rows.Count == 0)
            return Result.Failure<BulkValidationResultDto>(new Error("Bulk.Empty", "Nothing to save."));
        if (rows.Count > MaxRows)
            return Result.Failure<BulkValidationResultDto>(new Error("Bulk.TooMany", $"At most {MaxRows} rows can be saved at once."));

        void Add(int i, string sev, string msg) => result.Issues.Add(new BulkRowIssueDto { Index = i, Severity = sev, Message = msg });

        var ids = rows.Select(r => r.AccountId).Concat(rows.Where(r => r.DestinationAccountId.HasValue).Select(r => r.DestinationAccountId!.Value)).Distinct().ToList();
        var accounts = await _context.Accounts.AsNoTracking()
            .Include(a => a.AccountCategory)
            .Where(a => a.UserId == request.UserId && ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        // Structural checks
        var valid = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!accounts.ContainsKey(r.AccountId)) { Add(i, "error", "Account not found."); continue; }
            if (accounts[r.AccountId].IsArchived) Add(i, "warning", "This account is archived.");
            if (r.Transaction.Amount <= 0) { Add(i, "error", "Amount must be greater than zero."); continue; }
            if (r.DestinationAccountId.HasValue)
            {
                if (r.DestinationAccountId == r.AccountId) { Add(i, "error", "A transfer needs two different accounts."); continue; }
                if (!accounts.ContainsKey(r.DestinationAccountId.Value)) { Add(i, "error", "Destination account not found."); continue; }
            }
            if (r.Transaction.Date.Date > DateTime.UtcNow.Date.AddDays(1)) Add(i, "warning", "Dated in the future — it will change today's balance immediately.");
            valid.Add(i);
        }

        // Running-balance replay, chronological, credits before debits on the same day.
        var running = accounts.ToDictionary(a => a.Key, a => a.Value.Balance);
        bool IsLiab(int id) => accounts[id].AccountCategory?.IsLiability ?? false;
        foreach (var i in valid.OrderBy(i => rows[i].Transaction.Date.Date)
                                .ThenBy(i => (rows[i].DestinationAccountId.HasValue || rows[i].Transaction.Type == TransactionType.Expense) ? 1 : 0)
                                .ThenBy(i => i))
        {
            var r = rows[i];
            var amount = r.Transaction.Amount;
            var isDebit = r.DestinationAccountId.HasValue || r.Transaction.Type == TransactionType.Expense;
            running[r.AccountId] += isDebit ? -amount : amount;
            if (r.DestinationAccountId.HasValue) running[r.DestinationAccountId.Value] += amount;

            if (isDebit && !IsLiab(r.AccountId) && running[r.AccountId] < 0)
                Add(i, "error", $"Not enough balance in {accounts[r.AccountId].Name}: after the rows dated before or on this one it would be ₹{running[r.AccountId]:F2}. Add the missing credit, fix the date, or correct the amount.");
        }

        // Duplicates inside the batch
        var seen = new Dictionary<string, int>();
        foreach (var i in valid)
        {
            var r = rows[i];
            var key = $"{r.AccountId}|{r.DestinationAccountId}|{r.Transaction.Date.Date:yyyyMMdd}|{r.Transaction.Amount}|{r.Transaction.Type}|{(r.Transaction.Description ?? "").Trim().ToLowerInvariant()}";
            if (seen.TryGetValue(key, out var first)) Add(i, "warning", $"Same as row {first + 1} (account, date, amount, description).");
            else seen[key] = i;
        }

        // Possible duplicates of what is already saved
        if (valid.Count > 0)
        {
            var accIds = valid.Select(i => rows[i].AccountId).Distinct().ToList();
            var min = valid.Min(i => rows[i].Transaction.Date.Date).AddDays(-1);
            var max = valid.Max(i => rows[i].Transaction.Date.Date).AddDays(2);
            var existing = await _context.Transactions.AsNoTracking()
                .Where(t => accIds.Contains(t.AccountId) && t.Date >= min && t.Date <= max)
                .Select(t => new { t.AccountId, t.Date, t.Amount, t.Type, t.Description })
                .ToListAsync(ct);
            foreach (var i in valid)
            {
                var r = rows[i];
                var type = r.DestinationAccountId.HasValue ? TransactionType.Expense : r.Transaction.Type;
                var hit = existing.FirstOrDefault(e => e.AccountId == r.AccountId && e.Amount == r.Transaction.Amount &&
                                                       e.Type == type && Math.Abs((e.Date.Date - r.Transaction.Date.Date).TotalDays) <= 1);
                if (hit != null)
                    Add(i, "warning", $"Looks like an entry you already have: \"{hit.Description}\" ₹{hit.Amount:F2} on {hit.Date:dd MMM}.");
            }
        }

        result.Issues = result.Issues.OrderBy(x => x.Index).ThenBy(x => x.Severity == "error" ? 0 : 1).ToList();
        return Result.Success(result);
    }
}
