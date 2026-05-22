using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Contracts;

public interface IApplicationDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<AccountCategory> AccountCategories { get; }
    DbSet<TransactionCategory> TransactionCategories { get; }
    DbSet<Feedback> Feedbacks { get; }
    DbSet<Budget> Budgets { get; }
    DbSet<RecurringTransaction> RecurringTransactions { get; }
    DbSet<Subscription> Subscriptions { get; }

    DbSet<Issue> Issues { get; }
    DbSet<IssueTaxonomy> IssueTaxonomies { get; }
    DbSet<IssueComment> IssueComments { get; }
    DbSet<IssueVote> IssueVotes { get; }
    DbSet<CommentVote> CommentVotes { get; }
    DbSet<IssueLabel> IssueLabels { get; }
    DbSet<IssueLabelAssignment> IssueLabelAssignments { get; }
    DbSet<IssueMilestone> IssueMilestones { get; }
    DbSet<IssueAssignee> IssueAssignees { get; }
    DbSet<CommentReaction> CommentReactions { get; }
    DbSet<IssueAttachment> IssueAttachments { get; }
    DbSet<UserGamificationProfile> UserGamificationProfiles { get; }
    DbSet<Badge> Badges { get; }
    DbSet<UserBadge> UserBadges { get; }
    DbSet<IssueStatusHistory> IssueStatusHistories { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
