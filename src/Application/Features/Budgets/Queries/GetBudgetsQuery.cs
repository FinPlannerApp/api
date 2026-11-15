using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetsQuery(string UserId, QueryParameters QueryParams) : IRequest<Result<PaginatedResult<BudgetDto>>>;
 
public class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, Result<PaginatedResult<BudgetDto>>>
{
     private readonly IApplicationDbContext _context;
 
     public GetBudgetsQueryHandler(IApplicationDbContext context)
     {
         _context = context;
     }
 
     public async Task<Result<PaginatedResult<BudgetDto>>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
     {
        var query = _context.Budgets
            .Where(b => b.UserId == request.UserId)
            .Include(b => b.TransactionCategory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.QueryParams.GlobalSearch))
        {
            var search = request.QueryParams.GlobalSearch.Trim().ToLower();
            query = query.Where(b => b.TransactionCategory != null && b.TransactionCategory.Name.ToLower().Contains(search));
        }

        // Default order (category A-Z, "Overall" for the all-spending budget) continues by cursor: no OFFSET, no COUNT after the first page.
        if (request.QueryParams.SortBy == null)
        {
            var seeking = KeysetPager.TryDecodeCursor<string>(request.QueryParams.Cursor, out _, out _);
            var total = seeking ? -1 : await query.CountAsync(cancellationToken);
            var page = await query.ToKeysetPageAsync(b => b.TransactionCategory != null ? b.TransactionCategory.Name : "Overall", b => b.Id,
                false, false, request.QueryParams.Cursor, request.QueryParams.PageSize,
                skip: (request.QueryParams.PageNumber - 1) * request.QueryParams.PageSize, ct: cancellationToken);
            var dtos = page.Items.Select(b => new BudgetDto
            {
                Id = b.Id,
                TransactionCategoryId = b.TransactionCategoryId,
                CategoryName = b.TransactionCategory != null ? b.TransactionCategory.Name : null,
                Amount = b.Amount,
                Period = b.Period,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                AlertThresholdPercent = b.AlertThresholdPercent
            }).ToList();
            return Result.Success(new PaginatedResult<BudgetDto>(dtos, total, request.QueryParams.PageNumber, request.QueryParams.PageSize)
            { HasMore = page.HasMore, NextCursor = page.NextCursor });
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        // Sorting
        query = request.QueryParams.SortBy?.ToLower() switch
        {
            "amount" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(b => b.Amount) : query.OrderBy(b => b.Amount),
            "category" or "categoryname" => request.QueryParams.SortOrder == "desc" 
                ? query.OrderByDescending(b => b.TransactionCategory != null ? b.TransactionCategory.Name : string.Empty) 
                : query.OrderBy(b => b.TransactionCategory != null ? b.TransactionCategory.Name : string.Empty),
            "startdate" => request.QueryParams.SortOrder == "desc" ? query.OrderByDescending(b => b.StartDate) : query.OrderBy(b => b.StartDate),
            _ => query.OrderBy(b => b.TransactionCategory != null ? b.TransactionCategory.Name : "Overall")
        };

        var items = await query
            .Skip((request.QueryParams.PageNumber - 1) * request.QueryParams.PageSize)
            .Take(request.QueryParams.PageSize)
            .Select(b => new BudgetDto
            {
                Id = b.Id,
                TransactionCategoryId = b.TransactionCategoryId,
                CategoryName = b.TransactionCategory != null ? b.TransactionCategory.Name : null,
                Amount = b.Amount,
                Period = b.Period,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                AlertThresholdPercent = b.AlertThresholdPercent
            })
            .ToListAsync(cancellationToken);

        var result = new PaginatedResult<BudgetDto>(items, totalRecords, request.QueryParams.PageNumber, request.QueryParams.PageSize);
        return Result.Success(result);
     }
}
