using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Transactions.Queries;

public record GetUndoableTransactionsQuery(string UserId) : IRequest<Result<List<UndoableTransactionDto>>>;

/// <summary>Recently deleted plain transactions that can still be restored, each with the effect of restoring it.</summary>
public class GetUndoableTransactionsQueryHandler : IRequestHandler<GetUndoableTransactionsQuery, Result<List<UndoableTransactionDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ISender _sender;

    public GetUndoableTransactionsQueryHandler(IApplicationDbContext context, ISender sender)
    {
        _context = context;
        _sender = sender;
    }

    public async Task<Result<List<UndoableTransactionDto>>> Handle(GetUndoableTransactionsQuery request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - UndoRules.Window;

        var deleted = await _context.Transactions.IgnoreQueryFilters()
            .Include(t => t.Account).ThenInclude(a => a.AccountCategory)
            .Include(t => t.TransactionCategory)
            .Where(t => t.UserId == request.UserId && t.IsDeleted && t.DeletedAt != null && t.DeletedAt >= cutoff &&
                        t.TransferGroupId == null && t.Kind == TransactionKind.Normal)
            .OrderByDescending(t => t.DeletedAt)
            .ToListAsync(ct);

        if (deleted.Count == 0) return Result.Success(new List<UndoableTransactionDto>());

        // Entries that belong to a card payment come back only together with the payment, so they are not offered.
        var ids = deleted.Select(t => t.Id).ToList();
        var linked = await _context.CreditCardPayments.IgnoreQueryFilters()
            .Where(p => (p.CashbackTransactionId != null && ids.Contains(p.CashbackTransactionId.Value)) ||
                        (p.InterestTransactionId != null && ids.Contains(p.InterestTransactionId.Value)) ||
                        (p.PrincipalIncomeTransactionId != null && ids.Contains(p.PrincipalIncomeTransactionId.Value)) ||
                        (p.PrincipalExpenseTransactionId != null && ids.Contains(p.PrincipalExpenseTransactionId.Value)))
            .Select(p => new { p.CashbackTransactionId, p.InterestTransactionId, p.PrincipalIncomeTransactionId, p.PrincipalExpenseTransactionId })
            .ToListAsync(ct);
        var linkedIds = new HashSet<int>();
        foreach (var p in linked)
            foreach (var id in new[] { p.CashbackTransactionId, p.InterestTransactionId, p.PrincipalIncomeTransactionId, p.PrincipalExpenseTransactionId })
                if (id.HasValue) linkedIds.Add(id.Value);

        var result = new List<UndoableTransactionDto>();
        // Budget progress is computed once per distinct date.
        var progressByDate = new Dictionary<DateTime, List<DTOs.Budgets.BudgetProgressDto>>();
        // Several deletions on one account stack up in the preview, newest-deleted first being undone last.
        foreach (var t in deleted.Where(t => !linkedIds.Contains(t.Id)))
        {
            var acc = t.Account;
            var liability = acc.AccountCategory?.IsLiability ?? false;
            var delta = t.Type == TransactionType.Income ? t.Amount : -t.Amount;
            var after = acc.Balance + delta;
            var dto = new UndoableTransactionDto
            {
                TransactionId = t.Id, AccountId = t.AccountId, AccountName = acc.Name,
                Description = t.Description, Amount = t.Amount, Type = t.Type, Date = t.Date,
                CategoryName = t.TransactionCategory?.Name,
                DeletedAt = t.DeletedAt!.Value,
                SecondsLeft = Math.Max(0, (int)(t.DeletedAt.Value + UndoRules.Window - now).TotalSeconds),
                BalanceNow = acc.Balance, BalanceAfter = after, IsLiability = liability
            };
            if (!liability && after < 0)
            {
                dto.CanUndo = false;
                dto.BlockedReason = $"Restoring this would take {acc.Name} below zero.";
            }

            if (t.Type == TransactionType.Expense && t.TransactionCategoryId.HasValue)
            {
                var key = t.Date.Date;
                if (!progressByDate.TryGetValue(key, out var progress))
                {
                    var pr = await _sender.Send(new Features.Budgets.Queries.GetBudgetProgressQuery(request.UserId, t.Date), ct);
                    progress = pr.IsSuccess ? pr.Value : new();
                    progressByDate[key] = progress;
                }
                var b = progress.FirstOrDefault(p => p.TransactionCategoryId == t.TransactionCategoryId);
                if (b != null)
                    dto.Budget = new UndoBudgetImpactDto
                    {
                        Name = b.CategoryName ?? "Overall",
                        BudgetAmount = b.BudgetAmount,
                        SpentNow = b.SpentAmount,
                        SpentAfter = b.SpentAmount + t.Amount,
                        AlertThresholdPercent = b.AlertThresholdPercent
                    };
            }
            result.Add(dto);
        }

        return Result.Success(result);
    }
}
