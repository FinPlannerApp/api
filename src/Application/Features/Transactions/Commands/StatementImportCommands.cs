using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Transactions.Commands;

public record PreviewStatementImportCommand(string UserId, StatementImportPreviewRequestDto Request)
    : IRequest<Result<StatementImportPreviewDto>>;

public record CommitStatementImportCommand(string UserId, StatementImportCommitDto Request)
    : IRequest<Result<StatementImportResultDto>>;

internal static class StatementImportRules
{
    public const int MaxRows = 500;
    public const int DateToleranceDays = 2;

    public static string? CleanRef(string? r)
    {
        r = r?.Trim();
        if (string.IsNullOrEmpty(r)) return null;
        return r.Length > 64 ? r[..64] : r;
    }

    public static HashSet<string> Tokens(string? s) =>
        (s ?? "").ToLowerInvariant()
            .Split(new[] { ' ', '/', '-', '_', '*', '@', '.', ',', ':', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2 && !t.All(char.IsDigit))
            .ToHashSet();

    public static double Similarity(string? a, string? b)
    {
        var x = Tokens(a); var y = Tokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return (double)x.Intersect(y).Count() / x.Union(y).Count();
    }
}

/// <summary>Compares statement rows with what is already saved for the account. Writes nothing.</summary>
public class PreviewStatementImportCommandHandler
    : IRequestHandler<PreviewStatementImportCommand, Result<StatementImportPreviewDto>>
{
    private readonly IApplicationDbContext _context;
    public PreviewStatementImportCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<StatementImportPreviewDto>> Handle(PreviewStatementImportCommand request, CancellationToken ct)
    {
        var req = request.Request;
        if (req.Rows.Count == 0)
            return Result.Failure<StatementImportPreviewDto>(new Error("Import.Empty", "The statement has no rows."));
        if (req.Rows.Count > StatementImportRules.MaxRows)
            return Result.Failure<StatementImportPreviewDto>(new Error("Import.TooMany", $"Import at most {StatementImportRules.MaxRows} rows at a time (about one month)."));

        var owns = await _context.Accounts.AsNoTracking().AnyAsync(a => a.Id == req.AccountId && a.UserId == request.UserId, ct);
        if (!owns) return Result.Failure<StatementImportPreviewDto>(new Error("Import.AccountNotFound", "Account not found."));

        var min = req.Rows.Min(r => r.Date.Date).AddDays(-StatementImportRules.DateToleranceDays - 1);
        var max = req.Rows.Max(r => r.Date.Date).AddDays(StatementImportRules.DateToleranceDays + 2);
        var existing = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == request.UserId && t.AccountId == req.AccountId
                        && t.Date >= min && t.Date <= max
                        && !t.IsOpeningBalance && !t.IsBalanceAdjustment)
            .Select(t => new { t.Id, t.Date, t.Amount, t.Type, t.Description, t.ImportRef })
            .ToListAsync(ct);

        var used = new HashSet<int>();
        var result = new StatementImportPreviewDto();
        var matches = req.Rows.Select((_, i) => new StatementRowMatchDto { Index = i }).ToList();

        // 1. Already imported (exact bank reference) and repeats of a reference inside the file.
        var seenRefs = new HashSet<string>();
        for (var i = 0; i < req.Rows.Count; i++)
        {
            var rf = StatementImportRules.CleanRef(req.Rows[i].Ref);
            if (rf == null) continue;
            if (!seenRefs.Add(rf))
            {
                matches[i].Status = "Duplicate";
                matches[i].MatchedDescription = "Repeated reference inside this file";
                continue;
            }
            var hit = existing.FirstOrDefault(e => e.ImportRef == rf);
            if (hit != null)
            {
                used.Add(hit.Id);
                matches[i].Status = "Imported";
                matches[i].MatchedTransactionId = hit.Id;
                matches[i].MatchedDescription = hit.Description;
                matches[i].MatchedDate = hit.Date;
            }
        }

        bool Open(int i) => matches[i].Status == "New";

        // 2. Same amount, type and day.
        for (var i = 0; i < req.Rows.Count; i++)
        {
            if (!Open(i)) continue;
            var r = req.Rows[i];
            var hit = existing
                .Where(e => !used.Contains(e.Id) && e.ImportRef == null && e.Amount == r.Amount && e.Type == r.Type && e.Date.Date == r.Date.Date)
                .OrderByDescending(e => StatementImportRules.Similarity(e.Description, r.Description))
                .FirstOrDefault();
            if (hit == null) continue;
            used.Add(hit.Id);
            matches[i].Status = "Duplicate";
            matches[i].MatchedTransactionId = hit.Id;
            matches[i].MatchedDescription = hit.Description;
            matches[i].MatchedDate = hit.Date;
        }

        // 3. Same amount and type within a couple of days: ask the user.
        for (var i = 0; i < req.Rows.Count; i++)
        {
            if (!Open(i)) continue;
            var r = req.Rows[i];
            var hit = existing
                .Where(e => !used.Contains(e.Id) && e.ImportRef == null && e.Amount == r.Amount && e.Type == r.Type
                            && Math.Abs((e.Date.Date - r.Date.Date).TotalDays) <= StatementImportRules.DateToleranceDays)
                .OrderBy(e => Math.Abs((e.Date.Date - r.Date.Date).TotalDays))
                .ThenByDescending(e => StatementImportRules.Similarity(e.Description, r.Description))
                .FirstOrDefault();
            if (hit == null) continue;
            used.Add(hit.Id);
            matches[i].Status = "Possible";
            matches[i].MatchedTransactionId = hit.Id;
            matches[i].MatchedDescription = hit.Description;
            matches[i].MatchedDate = hit.Date;
        }

        result.Matches = matches;
        return Result.Success(result);
    }
}

/// <summary>Applies the user's per-row choices. Adds go through the normal bulk path (one transaction,
/// balance checks, idempotent batch id); merges only stamp the existing entry as reconciled.</summary>
public class CommitStatementImportCommandHandler
    : IRequestHandler<CommitStatementImportCommand, Result<StatementImportResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionService _transactions;
    public CommitStatementImportCommandHandler(IApplicationDbContext context, ITransactionService transactions)
    { _context = context; _transactions = transactions; }

    public async Task<Result<StatementImportResultDto>> Handle(CommitStatementImportCommand request, CancellationToken ct)
    {
        var req = request.Request;
        if (req.Rows.Count == 0)
            return Result.Failure<StatementImportResultDto>(new Error("Import.Empty", "Nothing to import."));
        if (req.Rows.Count > StatementImportRules.MaxRows)
            return Result.Failure<StatementImportResultDto>(new Error("Import.TooMany", $"Import at most {StatementImportRules.MaxRows} rows at a time."));

        var owns = await _context.Accounts.AsNoTracking().AnyAsync(a => a.Id == req.AccountId && a.UserId == request.UserId, ct);
        if (!owns) return Result.Failure<StatementImportResultDto>(new Error("Import.AccountNotFound", "Account not found."));

        var outcome = new StatementImportResultDto();
        var now = DateTime.UtcNow;

        // Which references are already saved for this account (a reference can only be imported once).
        var refs = req.Rows.Select(r => StatementImportRules.CleanRef(r.Ref)).Where(r => r != null).Select(r => r!).Distinct().ToList();
        var takenRefs = refs.Count == 0 ? new HashSet<string>() :
            (await _context.Transactions.AsNoTracking()
                .Where(t => t.AccountId == req.AccountId && t.ImportRef != null && refs.Contains(t.ImportRef))
                .Select(t => t.ImportRef!).ToListAsync(ct)).ToHashSet();

        var adds = new List<BulkTransactionEntryDto>();
        var merges = new List<(int TxId, string? Ref)>();
        foreach (var r in req.Rows)
        {
            var action = (r.Action ?? "Skip").Trim();
            var rf = StatementImportRules.CleanRef(r.Ref);
            if (action.Equals("Add", StringComparison.OrdinalIgnoreCase))
            {
                if (r.Amount <= 0) { outcome.Skipped++; continue; }
                if (rf != null && !takenRefs.Add(rf)) { outcome.Skipped++; continue; } // already imported / repeated in file
                adds.Add(new BulkTransactionEntryDto
                {
                    AccountId = req.AccountId,
                    Transaction = new UpsertTransactionDto
                    {
                        Description = string.IsNullOrWhiteSpace(r.Description) ? "Bank statement entry" : r.Description.Trim(),
                        Amount = r.Amount,
                        Date = r.Date,
                        Type = r.Type,
                        TransactionCategoryId = r.CategoryId,
                        ImportRef = rf,
                        ReconciledAt = now
                    }
                });
            }
            else if (action.Equals("Merge", StringComparison.OrdinalIgnoreCase) && r.MergeTransactionId.HasValue)
            {
                if (rf != null && !takenRefs.Add(rf)) { outcome.Skipped++; continue; }
                merges.Add((r.MergeTransactionId.Value, rf));
            }
            else outcome.Skipped++;
        }

        if (adds.Count > 0)
        {
            var bulk = await _transactions.BulkUpsertTransactionsAsync(request.UserId, new BulkTransactionPayloadDto
            {
                Transactions = adds,
                BatchId = string.IsNullOrWhiteSpace(req.BatchId) ? null : "import::" + req.BatchId
            });
            if (bulk.IsFailure) return Result.Failure<StatementImportResultDto>(bulk.Error);
            if (bulk.Value.RolledBack)
            {
                outcome.Failures = bulk.Value.FailedTransactions;
                return Result.Failure<StatementImportResultDto>(new Error("Import.RolledBack",
                    "Nothing was imported: some rows could not be saved (see the list). Fix or skip them and try again."));
            }
            outcome.Added = bulk.Value.SuccessfulCount;
        }

        if (merges.Count > 0)
        {
            var ids = merges.Select(m => m.TxId).Distinct().ToList();
            var rows = await _context.Transactions
                .Where(t => t.UserId == request.UserId && t.AccountId == req.AccountId && ids.Contains(t.Id))
                .ToListAsync(ct);
            foreach (var (txId, rf) in merges)
            {
                var t = rows.FirstOrDefault(x => x.Id == txId);
                if (t == null) { outcome.Skipped++; continue; }
                t.ReconciledAt = now;
                if (rf != null && t.ImportRef == null) t.ImportRef = rf;
                outcome.Merged++;
            }
            await _context.SaveChangesAsync(ct);
        }

        return Result.Success(outcome);
    }
}
