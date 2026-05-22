namespace Domain.Entities;

public class IssueVote
{
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public required string UserId { get; set; }
    
    public int Value { get; set; } = 1; // 1 for upvote, -1 for downvote
}
