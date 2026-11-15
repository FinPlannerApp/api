
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Feedback;
using Domain.Entities;

namespace Application.Services;

public class FeedbackService : IFeedbackService
{
    private readonly IApplicationDbContext _context;
    public FeedbackService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<bool>> CreateFeedbackAsync(CreateFeedbackDto dto)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        var email = dto.Email?.Trim() ?? string.Empty;
        var type = dto.Type?.Trim() ?? string.Empty;
        var description = dto.Description?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100 || type.Length is < 1 or > 40 ||
            description.Length is < 3 or > 4000 || email.Length is < 3 or > 254 || !email.Contains('@'))
            return Result.Failure<bool>(new Error("Feedback.Invalid", "Please fill in every field (message up to 4000 characters)."));

        var feedback = new Feedback
        {
            Name = name,
            Email = email,
            Type = type,
            Description = description
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }
}