using Application.Contracts;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Security.Claims;
using System.Text.Json;

namespace Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // --- DbSets for Main Entities ---
    public DbSet<Account> Accounts { get; set; }
    public DbSet<AccountCategory> AccountCategories { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<TransactionCategory> TransactionCategories { get; set; }
    public DbSet<Feedback> Feedbacks { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<RecurringTransaction> RecurringTransactions { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }

    // --- DbSets for Log Entities ---
    public DbSet<AccountLog> AccountLogs { get; set; }
    public DbSet<AccountCategoryLog> AccountCategoryLogs { get; set; }
    public DbSet<TransactionLog> TransactionLogs { get; set; }
    public DbSet<TransactionCategoryLog> TransactionCategoryLogs { get; set; }
    public DbSet<FeedbackLog> FeedbackLogs { get; set; }

    // --- DbSets for Issue System ---
    public DbSet<Issue> Issues { get; set; }
    public DbSet<IssueTaxonomy> IssueTaxonomies { get; set; }
    public DbSet<IssueComment> IssueComments { get; set; }
    public DbSet<IssueVote> IssueVotes { get; set; }
    public DbSet<CommentVote> CommentVotes { get; set; }
    public DbSet<IssueLabel> IssueLabels { get; set; }
    public DbSet<IssueLabelAssignment> IssueLabelAssignments { get; set; }
    public DbSet<IssueMilestone> IssueMilestones { get; set; }
    public DbSet<IssueAssignee> IssueAssignees { get; set; }
    public DbSet<CommentReaction> CommentReactions { get; set; }
    public DbSet<IssueAttachment> IssueAttachments { get; set; }
    public DbSet<UserGamificationProfile> UserGamificationProfiles { get; set; }
    public DbSet<Badge> Badges { get; set; }
    public DbSet<UserBadge> UserBadges { get; set; }
    public DbSet<IssueStatusHistory> IssueStatusHistories { get; set; }
    public DbSet<IssueRelation> IssueRelations { get; set; }
    public DbSet<IssueActivity> IssueActivities { get; set; }

    public DbSet<ChallengeDay> ChallengeDays { get; set; }
    public DbSet<UserChallengeEnrollment> UserChallengeEnrollments { get; set; }
    public DbSet<UserChallengeProgress> UserChallengeProgresses { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = OnBeforeSaveChanges();
        SetAuditProperties();
        var result = await base.SaveChangesAsync(cancellationToken);
        await OnAfterSaveChanges(auditEntries);
        return result;
    }

    private void SetAuditProperties()
    {
        // FIX: Track IAuditable instead of BaseEntity to catch ApplicationUser too
        var entries = ChangeTracker.Entries<IAuditable>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        // FIX: Use IAuditable here as well
        var entries = ChangeTracker.Entries<IAuditable>().ToList();
        var auditEntries = new List<AuditEntry>();
        var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var auditEntry = new AuditEntry(entry) { TableName = entry.Metadata.GetTableName()!, UserId = userId };

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary) continue;
                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[propertyName] = property.CurrentValue!;
                    continue;
                }
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.Action = "CREATE";
                        auditEntry.NewValues[propertyName] = property.CurrentValue!;
                        break;
                    case EntityState.Deleted:
                        auditEntry.Action = "DELETE";
                        auditEntry.OldValues[propertyName] = property.OriginalValue!;
                        break;
                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.Action = "UPDATE";
                            auditEntry.OldValues[propertyName] = property.OriginalValue!;
                            auditEntry.NewValues[propertyName] = property.CurrentValue!;
                        }
                        break;
                }
            }
            if (!string.IsNullOrEmpty(auditEntry.Action))
            {
                auditEntries.Add(auditEntry);
            }
        }
        return auditEntries;
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0)
            return;

        foreach (var auditEntry in auditEntries)
        {
            // FIX: Check if entity is supported for logging BEFORE accessing ID
            AuditLog? log = auditEntry.Entry.Entity switch
            {
                Account _ => new AccountLog(),
                AccountCategory _ => new AccountCategoryLog(),
                Transaction _ => new TransactionLog(),
                TransactionCategory _ => new TransactionCategoryLog(),
                Feedback _ => new FeedbackLog(),
                _ => null
            };

            if (log == null) continue;

            // Safe to access ID now
            int entityId = Convert.ToInt32(auditEntry.Entry.Property("Id").CurrentValue);
            var oldData = auditEntry.OldValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.OldValues);
            var newData = auditEntry.NewValues.Count == 0 ? null : JsonSerializer.Serialize(auditEntry.NewValues);

            log.EntityId = entityId;
            log.Action = auditEntry.Action;
            log.OldData = oldData;
            log.NewData = newData;
            log.ChangedByUserId = auditEntry.UserId;
            log.ChangedAt = DateTime.UtcNow;

            await this.AddAsync(log);
        }
        // Save the log entries in a separate transaction
        await base.SaveChangesAsync();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // --- IDENTITY SCHEMA ---
        builder.Entity<ApplicationUser>(e => {
            e.ToTable("Users", "identity");
            e.OwnsMany(p => p.RefreshTokens, a =>
            {
                a.ToTable("RefreshTokens", "identity");
                a.WithOwner().HasForeignKey("UserId");
            });
        });
        builder.Entity<IdentityRole>(e => e.ToTable("Roles", "identity"));
        builder.Entity<IdentityUserRole<string>>(e => e.ToTable("UserRoles", "identity"));
        builder.Entity<IdentityUserClaim<string>>(e => e.ToTable("UserClaims", "identity"));
        builder.Entity<IdentityUserLogin<string>>(e => e.ToTable("UserLogins", "identity"));
        builder.Entity<IdentityRoleClaim<string>>(e => e.ToTable("RoleClaims", "identity"));
        builder.Entity<IdentityUserToken<string>>(e => e.ToTable("UserTokens", "identity"));

        // --- ACCOUNTS SCHEMA ---
        builder.Entity<Account>(e =>
        {
            e.ToTable("Accounts", "accounts");
            // e.HasQueryFilter(a => !a.IsDeleted); // Moved to global filters
        });
        builder.Entity<AccountCategory>(e =>
        {
            e.ToTable("AccountCategories", "accounts");
            e.HasIndex(ac => new { ac.UserId, ac.Name }).IsUnique();
            // e.HasQueryFilter(ac => !ac.IsDeleted); // Moved to global filters
        });
        builder.Entity<AccountLog>(e => e.ToTable("AccountLogs", "accounts"));
        builder.Entity<AccountCategoryLog>(e => e.ToTable("AccountCategoryLogs", "accounts"));

        // --- TRANSACTIONS SCHEMA ---
        builder.Entity<Transaction>(e =>
        {
            e.ToTable("Transactions", "transactions");
            // Soft delete filter — excluded from all queries unless explicitly ignored // Moved to global filters
            // e.HasQueryFilter(t => !t.IsDeleted);
            // Critical performance indexes for dashboard/list queries
            e.HasIndex(t => new { t.AccountId, t.Date });
            e.HasIndex(t => t.TransactionCategoryId);
            // Financial integrity: amounts must always be positive
            e.ToTable(tb => tb.HasCheckConstraint("CK_Transaction_Amount_Positive", "\"Amount\" > 0"));
            
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TransactionCategory>(e =>
        {
            e.ToTable("TransactionCategories", "transactions");
            e.HasIndex(tc => new { tc.UserId, tc.Name }).IsUnique();
            // e.HasQueryFilter(tc => !tc.IsDeleted); // Moved to global filters
        });
        builder.Entity<TransactionLog>(e => e.ToTable("TransactionLogs", "transactions"));
        builder.Entity<TransactionCategoryLog>(e => e.ToTable("TransactionCategoryLogs", "transactions"));

        // --- FEEDBACK SCHEMA ---
        builder.Entity<Feedback>(e => e.ToTable("Feedbacks", "feedback"));
        builder.Entity<FeedbackLog>(e => e.ToTable("FeedbackLogs", "feedback"));

        // --- CHALLENGE SCHEMA ---
        builder.Entity<ChallengeDay>(e =>
        {
            e.ToTable("ChallengeDays", "challenges");
            e.HasIndex(cd => cd.DayNumber).IsUnique();
        });
        builder.Entity<UserChallengeEnrollment>(e =>
        {
            e.ToTable("UserChallengeEnrollments", "challenges");
            e.HasIndex(uce => uce.UserId).IsUnique();
        });
        builder.Entity<UserChallengeProgress>(e =>
        {
            e.ToTable("UserChallengeProgresses", "challenges");
            e.HasIndex(ucp => new { ucp.UserId, ucp.ChallengeDayId }).IsUnique();

            e.HasOne(ucp => ucp.ChallengeDay)
                .WithMany()
                .HasForeignKey(ucp => ucp.ChallengeDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Soft Delete Global Filters ---
        builder.Entity<Transaction>().HasQueryFilter(t => !t.IsDeleted);
        builder.Entity<Account>().HasQueryFilter(a => !a.IsDeleted);
        builder.Entity<AccountCategory>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<TransactionCategory>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<Budget>().HasQueryFilter(b => !b.IsDeleted);
        builder.Entity<RecurringTransaction>().HasQueryFilter(rt => !rt.IsDeleted);
        builder.Entity<Subscription>().HasQueryFilter(s => !s.IsDeleted);
        builder.Entity<Issue>().HasQueryFilter(i => !i.IsDeleted);
        builder.Entity<IssueComment>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<ChallengeDay>().HasQueryFilter(cd => !cd.IsDeleted);
        builder.Entity<UserChallengeEnrollment>().HasQueryFilter(uce => !uce.IsDeleted);
        builder.Entity<UserChallengeProgress>().HasQueryFilter(ucp => !ucp.IsDeleted);

        // --- Budget Configuration ---
        builder.Entity<Budget>()
            .Property(b => b.Amount)
            .HasColumnType("numeric(18,2)");

        builder.Entity<Budget>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Budget>()
            .HasOne(b => b.TransactionCategory)
            .WithMany()
            .HasForeignKey(b => b.TransactionCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Budget>()
            .HasIndex(b => new { b.UserId, b.TransactionCategoryId });
        
        builder.Entity<Budget>()
            .ToTable(b => b.HasCheckConstraint("CK_Budget_Amount", "\"Amount\" > 0"));

        // --- Recurring Transaction Configuration ---
        builder.Entity<RecurringTransaction>(e =>
        {
            e.ToTable("RecurringTransactions", "transactions");
            
            e.Property(rt => rt.Amount)
                .HasColumnType("numeric(18,2)");

            e.HasOne(rt => rt.Account)
                .WithMany()
                .HasForeignKey(rt => rt.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(rt => rt.TransactionCategory)
                .WithMany()
                .HasForeignKey(rt => rt.TransactionCategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(rt => new { rt.UserId, rt.NextProcessDate, rt.IsActive });
            
            e.ToTable(tb => tb.HasCheckConstraint("CK_RecurringTransaction_Amount_Positive", "\"Amount\" > 0"));
        });

        // --- Subscription Configuration ---
        builder.Entity<Subscription>(e =>
        {
            e.ToTable("Subscriptions", "transactions");

            e.HasOne(s => s.RecurringTransaction)
                .WithOne(rt => rt.Subscription)
                .HasForeignKey<Subscription>(s => s.RecurringTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
                
            e.HasIndex(s => new { s.UserId, s.Name }).IsUnique();
        });

        // --- ISSUE SCHEMA ---
        builder.Entity<Issue>(e =>
        {
            e.ToTable("Issues");
            e.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Subcategory).WithMany().HasForeignKey(i => i.SubcategoryId).OnDelete(DeleteBehavior.Restrict);
            
            // Enum → string conversions for backward compatibility with existing data
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);

            // Performance indexes
            e.HasIndex(i => i.Status);
            e.HasIndex(i => i.PainScore);
            e.HasIndex(i => i.PainVelocity);
            e.HasIndex(i => i.CreatedAt);
            e.HasIndex(i => i.Type);
            e.HasIndex(i => i.CategoryId);
            e.HasIndex(i => i.CreatorUserId);
            e.HasIndex(i => new { i.Status, i.PainScore }); // Composite for sorted listing
            e.HasIndex(i => new { i.Status, i.PainVelocity }); // Composite for sorted listing by velocity
        });

        builder.Entity<IssueTaxonomy>(e =>
        {
            e.ToTable("IssueTaxonomies");
            e.HasOne(t => t.Parent).WithMany(p => p.Children).HasForeignKey(t => t.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<IssueComment>(e =>
        {
            e.ToTable("IssueComments");
            e.HasOne(c => c.Issue).WithMany(i => i.Comments).HasForeignKey(c => c.IssueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.ParentComment).WithMany(p => p.Replies).HasForeignKey(c => c.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<IssueVote>(e =>
        {
            e.ToTable("IssueVotes");
            e.HasKey(v => new { v.IssueId, v.UserId });
        });

        builder.Entity<CommentVote>(e =>
        {
            e.ToTable("CommentVotes");
            e.HasKey(v => new { v.CommentId, v.UserId });
        });

        // --- Phase 2: Labels ---
        builder.Entity<IssueLabel>(e =>
        {
            e.ToTable("IssueLabels");
            e.HasIndex(l => l.Name).IsUnique();
        });

        builder.Entity<IssueLabelAssignment>(e =>
        {
            e.ToTable("IssueLabelAssignments");
            e.HasKey(la => new { la.IssueId, la.LabelId });
            e.HasOne(la => la.Issue).WithMany(i => i.Labels).HasForeignKey(la => la.IssueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(la => la.Label).WithMany(l => l.Issues).HasForeignKey(la => la.LabelId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Phase 2: Milestones ---
        builder.Entity<IssueMilestone>(e =>
        {
            e.ToTable("IssueMilestones");
        });

        builder.Entity<Issue>()
            .HasOne(i => i.Milestone)
            .WithMany(m => m.Issues)
            .HasForeignKey(i => i.MilestoneId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Phase 2: Assignees ---
        builder.Entity<IssueAssignee>(e =>
        {
            e.ToTable("IssueAssignees");
            e.HasKey(a => new { a.IssueId, a.UserId });
            e.HasOne(a => a.Issue).WithMany(i => i.Assignees).HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Phase 2: Comment Reactions ---
        builder.Entity<CommentReaction>(e =>
        {
            e.ToTable("CommentReactions");
            e.HasKey(r => new { r.CommentId, r.UserId, r.Emoji });
            e.HasOne(r => r.Comment).WithMany(c => c.Reactions).HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Cascade);
        });
        // --- Phase 3: Attachments ---
        builder.Entity<IssueAttachment>(e =>
        {
            e.ToTable("IssueAttachments");
            e.HasOne(a => a.Issue).WithMany(i => i.Attachments).HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Phase 3: Gamification ---
        builder.Entity<UserGamificationProfile>(e =>
        {
            e.ToTable("UserGamificationProfiles");
            e.HasKey(g => g.UserId);
        });

        builder.Entity<Badge>(e =>
        {
            e.ToTable("Badges");
            e.HasIndex(b => b.Name).IsUnique();
        });

        builder.Entity<UserBadge>(e =>
        {
            e.ToTable("UserBadges");
            e.HasKey(ub => new { ub.UserId, ub.BadgeId });
            e.HasOne(ub => ub.Badge).WithMany().HasForeignKey(ub => ub.BadgeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IssueStatusHistory>(e =>
        {
            e.ToTable("IssueStatusHistories");
            e.HasOne(h => h.Issue)
             .WithMany(i => i.StatusHistory)
             .HasForeignKey(h => h.IssueId)
             .OnDelete(DeleteBehavior.Cascade);
            
            // Enum → string conversions
            e.Property(h => h.OldStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(h => h.NewStatus).HasConversion<string>().HasMaxLength(20);
        });

        // --- IssueComment performance indexes and conversions ---
        builder.Entity<IssueComment>(e =>
        {
            e.HasIndex(c => c.IssueId);
            e.HasIndex(c => c.ParentCommentId);
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        });

        // --- Issue Relations ---
        builder.Entity<IssueRelation>(e =>
        {
            e.ToTable("IssueRelations");
            e.HasOne(r => r.Issue)
             .WithMany(i => i.Relations)
             .HasForeignKey(r => r.IssueId)
             .OnDelete(DeleteBehavior.Cascade);
             
            e.HasOne(r => r.TargetIssue)
             .WithMany()
             .HasForeignKey(r => r.TargetIssueId)
             .OnDelete(DeleteBehavior.Cascade);
             
            e.Property(r => r.RelationType).HasConversion<string>().HasMaxLength(20);
            
            e.HasIndex(r => new { r.IssueId, r.TargetIssueId, r.RelationType }).IsUnique();
        });

        // --- Issue Activities ---
        builder.Entity<IssueActivity>(e =>
        {
            e.ToTable("IssueActivities");
            e.HasOne(a => a.Issue)
             .WithMany()
             .HasForeignKey(a => a.IssueId)
             .OnDelete(DeleteBehavior.Cascade);
             
            e.HasIndex(a => a.IssueId);
            e.HasIndex(a => a.CreatedAt);
        });
    }
}