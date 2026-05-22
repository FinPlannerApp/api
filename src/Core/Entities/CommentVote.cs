namespace Domain.Entities;

public class CommentVote
{
    public int CommentId { get; set; }
    public IssueComment? Comment { get; set; }
    
    public required string UserId { get; set; }
    
    public int Value { get; set; } = 1; // 1 for upvote, -1 for downvote
}
