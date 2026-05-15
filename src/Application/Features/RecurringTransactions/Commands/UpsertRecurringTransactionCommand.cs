using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Commands;

public record UpsertRecurringTransactionCommand(string UserId, int? Id, UpsertRecurringTransactionDto Dto) : IRequest<Result<RecurringTransactionDto>>;

public class UpsertRecurringTransactionCommandHandler : IRequestHandler<UpsertRecurringTransactionCommand, Result<RecurringTransactionDto>>
{
    private readonly IApplicationDbContext _context;

    public UpsertRecurringTransactionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<RecurringTransactionDto>> Handle(UpsertRecurringTransactionCommand request, CancellationToken cancellationToken)
    {
        // Verify account exists and belongs to user
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == request.Dto.AccountId && a.UserId == request.UserId, cancellationToken);
        
        if (account == null)
        {
            return Result.Failure<RecurringTransactionDto>(new Error("Account.NotFound", "Account not found or access denied."));
        }

        RecurringTransaction? entity;

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            entity = await _context.RecurringTransactions
                .FirstOrDefaultAsync(rt => rt.Id == request.Id && rt.UserId == request.UserId, cancellationToken);

            if (entity == null)
            {
                return Result.Failure<RecurringTransactionDto>(new Error("RecurringTransaction.NotFound", "Recurring transaction not found."));
            }
        }
        else
        {
            entity = new RecurringTransaction
            {
                UserId = request.UserId,
                Description = request.Dto.Description // Set required property
            };
            _context.RecurringTransactions.Add(entity);
        }

        // entity is guaranteed non-null here
        entity.AccountId = request.Dto.AccountId;
        entity.TransactionCategoryId = request.Dto.TransactionCategoryId;
        entity.Description = request.Dto.Description;
        entity.Amount = request.Dto.Amount;
        entity.Type = request.Dto.Type;
        entity.Frequency = request.Dto.Frequency;
        entity.StartDate = request.Dto.StartDate;
        entity.EndDate = request.Dto.EndDate;
        entity.IsActive = request.Dto.IsActive;

        // If it's a new entity or the start date/frequency changed, recalculate NextProcessDate
        // Simplification: Recalculate if it's new or if it's currently in the future relative to StartDate
        if (request.Id == null || entity.NextProcessDate < entity.StartDate)
        {
            entity.NextProcessDate = entity.StartDate;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Fetch names manually for the DTO
        var accountName = await _context.Accounts
            .Where(a => a.Id == entity.AccountId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        string? categoryName = null;
        if (entity.TransactionCategoryId.HasValue)
        {
            categoryName = await _context.TransactionCategories
                .Where(tc => tc.Id == entity.TransactionCategoryId)
                .Select(tc => tc.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var resultDto = new RecurringTransactionDto
        {
            Id = entity.Id,
            AccountId = entity.AccountId,
            AccountName = accountName,
            TransactionCategoryId = entity.TransactionCategoryId,
            CategoryName = categoryName,
            Description = entity.Description,
            Amount = entity.Amount,
            Type = entity.Type,
            Frequency = entity.Frequency,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            NextProcessDate = entity.NextProcessDate,
            IsActive = entity.IsActive,
            LastProcessedDate = entity.LastProcessedDate
        };

        return Result.Success(resultDto);
    }
}
