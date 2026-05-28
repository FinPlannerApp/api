namespace Domain.Entities;

/// <summary>
/// Issue lifecycle status. Transitions are enforced by IssueWorkflowService.
/// </summary>
public enum IssueStatus
{
    New,
    Acknowledged,
    Triaged,
    Planned,
    InProgress,
    Released,
    Verified,
    Closed
}

/// <summary>
/// Severity levels used in pain score calculation.
/// </summary>
public enum IssueSeverity
{
    Minor,
    Major,
    Critical
}

/// <summary>
/// How often the issue occurs — used in pain score calculation.
/// </summary>
public enum IssueFrequency
{
    Rare,
    Sometimes,
    Frequent,
    Always
}

/// <summary>
/// Legacy priority dropdown (kept for backward compat, pain score is the real priority).
/// </summary>
public enum IssuePriority
{
    Low,
    Medium,
    High
}

/// <summary>
/// The type of issue (Bug, Feature request, or Question).
/// </summary>
public enum IssueType
{
    Bug,
    Feature,
    Question
}

/// <summary>
/// Defines bidirectional relations between issues.
/// </summary>
public enum IssueRelationType
{
    Blocks,
    BlockedBy,
    DuplicateOf,
    DuplicatedBy,
    RelatedTo,
    Causes,
    CausedBy,
    ParentOf,
    ChildOf
}

/// <summary>
/// Categories for structured comments.
/// </summary>
public enum CommentType
{
    General,
    Workaround,
    ReproSteps,
    Solution
}
