using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.DecisionJournal;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface IDecisionJournalService
{
    Task<Result<PaginatedResult<DecisionJournalEntryDto>>> GetPagedAsync(string userId, QueryParameters queryParams);
    Task<Result<DecisionJournalEntryDto>> UpsertAsync(string userId, UpsertDecisionJournalEntryDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
    Task<Result<DecisionJournalEntryDto>> RecordOutcomeAsync(string userId, RecordOutcomeDto dto);
}

public class DecisionJournalService : IDecisionJournalService
{
    private readonly IApplicationDbContext _context;

    public DecisionJournalService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaginatedResult<DecisionJournalEntryDto>>> GetPagedAsync(string userId, QueryParameters queryParams)
    {
        var queryable = _context.DecisionJournalEntries
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.DecisionDate)
            .AsQueryable();

        var totalRecords = await queryable.CountAsync();

        var items = await queryable
            .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
            .Take(queryParams.PageSize)
            .Select(d => MapToDto(d))
            .ToListAsync();

        return Result.Success(new PaginatedResult<DecisionJournalEntryDto>(
            items, totalRecords, queryParams.PageNumber, queryParams.PageSize));
    }

    public async Task<Result<DecisionJournalEntryDto>> UpsertAsync(string userId, UpsertDecisionJournalEntryDto dto)
    {
        DecisionJournalEntry entry;

        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.DecisionJournalEntries.FindAsync(dto.Id.Value);
            if (existing == null || existing.UserId != userId)
                return Result.Failure<DecisionJournalEntryDto>(new Error("DecisionJournal.NotFound", "Entry not found."));

            existing.Title = dto.Title;
            existing.Reasoning = dto.Reasoning;
            existing.Amount = dto.Amount;
            existing.DecisionDate = dto.DecisionDate;
            _context.DecisionJournalEntries.Update(existing);
            entry = existing;
        }
        else
        {
            entry = new DecisionJournalEntry
            {
                UserId = userId,
                Title = dto.Title,
                Reasoning = dto.Reasoning,
                Amount = dto.Amount,
                DecisionDate = dto.DecisionDate
            };
            _context.DecisionJournalEntries.Add(entry);
        }

        await _context.SaveChangesAsync();
        return Result.Success(MapToDto(entry));
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var entry = await _context.DecisionJournalEntries.FindAsync(id);
        if (entry == null || entry.UserId != userId)
            return Result.Failure<bool>(new Error("DecisionJournal.NotFound", "Entry not found."));

        entry.IsDeleted = true;
        entry.DeletedAt = DateTime.UtcNow;
        _context.DecisionJournalEntries.Update(entry);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<DecisionJournalEntryDto>> RecordOutcomeAsync(string userId, RecordOutcomeDto dto)
    {
        var entry = await _context.DecisionJournalEntries.FindAsync(dto.Id);
        if (entry == null || entry.UserId != userId)
            return Result.Failure<DecisionJournalEntryDto>(new Error("DecisionJournal.NotFound", "Entry not found."));

        entry.Outcome = dto.Outcome;
        entry.OutcomeRecordedAt = DateTime.UtcNow;
        _context.DecisionJournalEntries.Update(entry);
        await _context.SaveChangesAsync();

        return Result.Success(MapToDto(entry));
    }

    private static DecisionJournalEntryDto MapToDto(DecisionJournalEntry d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        Reasoning = d.Reasoning,
        Amount = d.Amount,
        DecisionDate = d.DecisionDate,
        Outcome = d.Outcome,
        OutcomeRecordedAt = d.OutcomeRecordedAt
    };
}
