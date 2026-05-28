using Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/issues/analytics")]
public class IssueAnalyticsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public IssueAnalyticsController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult> GetAnalytics()
    {
        var totalIssues = await _context.Issues.CountAsync();
        var openIssues = await _context.Issues.CountAsync(i => !i.IsClosed);
        var closedIssues = await _context.Issues.CountAsync(i => i.IsClosed);

        var issuesByStatus = await _context.Issues
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var issuesByType = await _context.Issues
            .GroupBy(i => i.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var ackIssues = await _context.Issues.Where(i => i.AcknowledgedAt != null).ToListAsync();
        var avgAckTime = ackIssues.Any() ? ackIssues.Average(i => (i.AcknowledgedAt!.Value - i.CreatedAt).TotalHours) : 0;

        var resolvedIssues = await _context.Issues.Where(i => i.ResolvedAt != null).ToListAsync();
        var avgResolveTime = resolvedIssues.Any() ? resolvedIssues.Average(i => (i.ResolvedAt!.Value - i.CreatedAt).TotalHours) : 0;

        return Ok(new
        {
            TotalIssues = totalIssues,
            OpenIssues = openIssues,
            ClosedIssues = closedIssues,
            IssuesByStatus = issuesByStatus,
            IssuesByType = issuesByType,
            AvgAckTimeHours = avgAckTime,
            AvgResolveTimeHours = avgResolveTime
        });
    }
}
