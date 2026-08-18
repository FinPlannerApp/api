using Domain.Entities.Split;

namespace Application.DTOs.Split;

public class UpdateExpenseDto
{
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Category { get; set; }
    public SplitType SplitType { get; set; }
    public List<ExpensePayerDto> Payers { get; set; } = new();
    public List<ExpenseParticipantDto> Participants { get; set; } = new();
}

public class DeleteExpenseResultDto
{
    public bool WasAlreadyImported { get; set; }
}
