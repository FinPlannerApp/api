namespace Domain.Entities;

public class IssueRelation : BaseEntity
{
    public int IssueId { get; set; }
    public Issue Issue { get; set; } = null!;

    public int TargetIssueId { get; set; }
    public Issue TargetIssue { get; set; } = null!;

    public IssueRelationType RelationType { get; set; }
}
