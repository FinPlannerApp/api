using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Queries;

public record GetRecurringTransactionsQuery(string UserId, QueryParameters QueryParams) : IRequest<Result<PaginatedResult<RecurringTransactionDto>>>;
 
public class GetRecurringTransactionsQueryHandler : IRequestHandler<GetRecurringTransactionsQuery, Result<PaginatedResult<RecurringTransactionDto>>>
{
     private readonly IApplicationDbContext _context;
 
     public GetRecurringTransactionsQueryHandler(IApplicationDbContext context)
     {
         _context = context;
     }
 
     public async Task<Result<PaginatedResult<RecurringTransactionDto>>> Handle(GetRecurringTransactionsQuery request, CancellationToken cancellationToken)
     {
        var query = _context.RecurringTransactions
            .Where(rt => rt.UserId == request.UserId)
            .Include(rt => rt.Account)
            .Include(rt => rt.TransactionCategory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.QueryParams.GlobalSearch))
        {
            var search = request.QueryParams.GlobalSearch.Trim().ToLower();
            query = query.Where(rt => 
                rt.Description.ToLower().Contains(search) || 
                rt.Account.Name.ToLower().Contains(search) ||
                (rt.TransactionCategory != null && rt.TransactionCategory.Name.ToLower().Contains(search)));
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        // Sorting
        query = request.QueryParams.SortBy?.ToLower() switch
        {
            "description" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(rt => rt.Description) : query.OrderBy(rt => rt.Description),
            "accountname" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(rt => rt.Account.Name) : query.OrderBy(rt => rt.Account.Name),
            "amount" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(rt => rt.Amount) : query.OrderBy(rt => rt.Amount),
            "frequency" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(rt => rt.Frequency) : query.OrderBy(rt => rt.Frequency),
            "nextprocessdate" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(rt => rt.NextProcessDate) : query.OrderBy(rt => rt.NextProcessDate),
            "category" or "categoryname" => request.QueryParams.SortOrder == "desc" 
                ? query.OrderByDescending(rt => rt.TransactionCategory != null ? rt.TransactionCategory.Name : string.Empty) 
                : query.OrderBy(rt => rt.TransactionCategory != null ? rt.TransactionCategory.Name : string.Empty),
            _ => query.OrderByDescending(rt => rt.IsActive).ThenByDescending(rt => rt.NextProcessDate)
        };

        var items = await query
            .Skip((request.QueryParams.PageNumber - 1) * request.QueryParams.PageSize)
            .Take(request.QueryParams.PageSize)
            .Select(rt => new RecurringTransactionDto
            {
                Id = rt.Id,
                AccountId = rt.AccountId,
                AccountName = rt.Account.Name,
                TransactionCategoryId = rt.TransactionCategoryId,
                CategoryName = rt.TransactionCategory != null ? rt.TransactionCategory.Name : null,
                Description = rt.Description,
                Amount = rt.Amount,
                Type = rt.Type,
                Frequency = rt.Frequency,
                CustomDays = rt.CustomDays,
                StartDate = rt.StartDate,
                EndDate = rt.EndDate,
                NextProcessDate = rt.NextProcessDate,
                IsActive = rt.IsActive,
                LastProcessedDate = rt.LastProcessedDate,
                IsObligation = rt.IsObligation,
                LinkedLoanAccountId = rt.LinkedLoanAccountId
            })
            .ToListAsync(cancellationToken);

        var result = new PaginatedResult<RecurringTransactionDto>(items, totalRecords, request.QueryParams.PageNumber, request.QueryParams.PageSize);
        return Result.Success(result);
     }
}
