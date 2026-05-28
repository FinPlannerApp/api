using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Service to manage bidirectional issue relationships (e.g. A blocks B, B blocked-by A).
/// </summary>
public class IssueRelationService
{
    private readonly IApplicationDbContext _context;
    private readonly IssueActivityService _activityService;

    public IssueRelationService(IApplicationDbContext context, IssueActivityService activityService)
    {
        _context = context;
        _activityService = activityService;
    }

    /// <summary>
    /// Gets all relationships for a given issue.
    /// </summary>
    public async Task<List<IssueRelation>> GetRelationsAsync(int issueId)
    {
        return await _context.IssueRelations
            .Include(r => r.TargetIssue)
            .Where(r => r.IssueId == issueId)
            .ToListAsync();
    }

    /// <summary>
    /// Links two issues bidirectionally. E.g. A blocks B and B is blocked-by A.
    /// </summary>
    public async Task<(bool Success, string? Error)> AddRelationAsync(int issueId, int targetIssueId, IssueRelationType relationType, string userId)
    {
        if (issueId == targetIssueId)
        {
            return (false, "An issue cannot relate to itself.");
        }

        var sourceExists = await _context.Issues.AnyAsync(i => i.Id == issueId);
        var targetExists = await _context.Issues.AnyAsync(i => i.Id == targetIssueId);

        if (!sourceExists || !targetExists)
        {
            return (false, "One or both issues do not exist.");
        }

        // Check if relation already exists
        var existing = await _context.IssueRelations
            .AnyAsync(r => r.IssueId == issueId && r.TargetIssueId == targetIssueId && r.RelationType == relationType);

        if (existing)
        {
            return (true, null); // Already linked
        }

        // Add source relation
        var sourceRelation = new IssueRelation
        {
            IssueId = issueId,
            TargetIssueId = targetIssueId,
            RelationType = relationType
        };
        _context.IssueRelations.Add(sourceRelation);

        // Add inverse relation for bidirectional integrity
        var inverseType = GetInverseRelationType(relationType);
        var existingInverse = await _context.IssueRelations
            .AnyAsync(r => r.IssueId == targetIssueId && r.TargetIssueId == issueId && r.RelationType == inverseType);

        if (!existingInverse)
        {
            var targetRelation = new IssueRelation
            {
                IssueId = targetIssueId,
                TargetIssueId = issueId,
                RelationType = inverseType
            };
            _context.IssueRelations.Add(targetRelation);
        }

        await _context.SaveChangesAsync();

        // Log activities asynchronously
        await _activityService.LogActivityAsync(issueId, userId, "RelationAdded", 
            $"Added relationship: {relationType} Issue #{targetIssueId}", new { targetIssueId, relationType = relationType.ToString() });
            
        await _activityService.LogActivityAsync(targetIssueId, userId, "RelationAdded", 
            $"Added relationship (inverse): {inverseType} Issue #{issueId}", new { targetIssueId = issueId, relationType = inverseType.ToString() });

        return (true, null);
    }

    /// <summary>
    /// Removes a bidirectional relation between two issues.
    /// </summary>
    public async Task<(bool Success, string? Error)> RemoveRelationAsync(int issueId, int targetIssueId, IssueRelationType relationType, string userId)
    {
        var relation = await _context.IssueRelations
            .FirstOrDefaultAsync(r => r.IssueId == issueId && r.TargetIssueId == targetIssueId && r.RelationType == relationType);

        if (relation == null)
        {
            return (false, "Relation not found.");
        }

        _context.IssueRelations.Remove(relation);

        // Find and remove the inverse relation
        var inverseType = GetInverseRelationType(relationType);
        var inverseRelation = await _context.IssueRelations
            .FirstOrDefaultAsync(r => r.IssueId == targetIssueId && r.TargetIssueId == issueId && r.RelationType == inverseType);

        if (inverseRelation != null)
        {
            _context.IssueRelations.Remove(inverseRelation);
        }

        await _context.SaveChangesAsync();

        // Log activities asynchronously
        await _activityService.LogActivityAsync(issueId, userId, "RelationRemoved", 
            $"Removed relationship: {relationType} Issue #{targetIssueId}", new { targetIssueId, relationType = relationType.ToString() });
            
        await _activityService.LogActivityAsync(targetIssueId, userId, "RelationRemoved", 
            $"Removed relationship (inverse): {inverseType} Issue #{issueId}", new { targetIssueId = issueId, relationType = inverseType.ToString() });

        return (true, null);
    }

    private IssueRelationType GetInverseRelationType(IssueRelationType relationType)
    {
        return relationType switch
        {
            IssueRelationType.Blocks => IssueRelationType.BlockedBy,
            IssueRelationType.BlockedBy => IssueRelationType.Blocks,
            IssueRelationType.DuplicateOf => IssueRelationType.DuplicatedBy,
            IssueRelationType.DuplicatedBy => IssueRelationType.DuplicateOf,
            IssueRelationType.Causes => IssueRelationType.CausedBy,
            IssueRelationType.CausedBy => IssueRelationType.Causes,
            IssueRelationType.ParentOf => IssueRelationType.ChildOf,
            IssueRelationType.ChildOf => IssueRelationType.ParentOf,
            IssueRelationType.RelatedTo => IssueRelationType.RelatedTo,
            _ => throw new ArgumentOutOfRangeException(nameof(relationType), relationType, null)
        };
    }
}
