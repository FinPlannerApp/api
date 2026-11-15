using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Issue;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

// --- Shared DTOs (used across controllers) ---

public class VoteDto
{
    public int Value { get; set; } // 1 for upvote, -1 for downvote, 0 for unvote
}

public class UpdateIssueDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public int? CategoryId { get; set; }
    public int? SubcategoryId { get; set; }
    public string? Severity { get; set; }
    public string? Frequency { get; set; }
    public bool? ImpactsMoney { get; set; }
    public decimal? FinancialImpactAmount { get; set; }
    public string? GitHubIssueUrl { get; set; }
    public int? MilestoneId { get; set; }
    public List<int>? LabelIds { get; set; }
    public List<string>? AssigneeUserIds { get; set; }
}

public class UpdateCommentDto
{
    public required string Content { get; set; }
}

public class UpdateStatusDto
{
    public required string Status { get; set; }
}

public class CreateLabelDto
{
    public required string Name { get; set; }
    public string Color { get; set; } = "#6366f1";
    public string? Description { get; set; }
}

public class CreateMilestoneDto
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
}

public class ReactionDto
{
    public required string Emoji { get; set; }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api")]
public class IssuesController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IssueRankingService _rankingService;
    private readonly IssueSimilarityService _similarityService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GamificationService _gamificationService;
    private readonly IssueWorkflowService _workflowService;
    private readonly IssueActivityService _activityService;

    public IssuesController(
        IApplicationDbContext context,
        IssueRankingService rankingService,
        IssueSimilarityService similarityService,
        UserManager<ApplicationUser> userManager,
        GamificationService gamificationService,
        IssueWorkflowService workflowService,
        IssueActivityService activityService)
    {
        _context = context;
        _rankingService = rankingService;
        _similarityService = similarityService;
        _userManager = userManager;
        _gamificationService = gamificationService;
        _workflowService = workflowService;
        _activityService = activityService;
    }

    // Helper: resolve user display name from userId
    private async Task<string> GetDisplayName(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "Anonymous";
        var user = await _userManager.FindByIdAsync(userId);
        return user?.Name ?? user?.UserName ?? userId[..Math.Min(8, userId.Length)] + "...";
    }

    private async Task<Dictionary<string, string>> GetDisplayNames(IEnumerable<string?> userIds)
    {
        var distinctIds = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var result = new Dictionary<string, string>();
        foreach (var id in distinctIds)
        {
            result[id!] = await GetDisplayName(id);
        }
        return result;
    }

    // ==================== ISSUES ====================

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetIssues(
        [FromQuery] string? status, 
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] string? severity,
        [FromQuery] string? sort = "pain", 
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 10,
        [FromQuery] string? cursor = null)
    {
        var query = _context.Issues
            .Include(i => i.Category)
            .Include(i => i.Subcategory)
            .Include(i => i.Labels).ThenInclude(la => la.Label)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<IssueStatus>(status, true, out var issueStatus))
            query = query.Where(i => i.Status == issueStatus);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<IssueType>(type, true, out var issueType))
            query = query.Where(i => i.Type == issueType);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(i => EF.Functions.ILike(i.Title, $"%{EscapeLike(search)}%") || EF.Functions.ILike(i.Description, $"%{EscapeLike(search)}%"));

        if (categoryId.HasValue)
            query = query.Where(i => i.CategoryId == categoryId.Value);

        if (!string.IsNullOrEmpty(severity) && Enum.TryParse<IssueSeverity>(severity, true, out var issueSeverity))
            query = query.Where(i => i.Severity == issueSeverity);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Every sort continues by cursor (sort key, then id): no OFFSET, and no COUNT after the first page.
        // Without a cursor the page number still works, so existing callers keep their behaviour.
        var skip = (page - 1) * pageSize;
        var seeking = !string.IsNullOrEmpty(cursor);
        var totalItems = 0;
        List<Domain.Entities.Issue> issues;
        bool hasMore;
        string? nextCursor;
        switch (sort)
        {
            case "date":
                (issues, hasMore, nextCursor, totalItems) = await PageIssuesAsync(query, i => i.CreatedAt);
                break;
            case "votes":
                (issues, hasMore, nextCursor, totalItems) = await PageIssuesAsync(query, i => i.Votes);
                break;
            case "velocity":
                (issues, hasMore, nextCursor, totalItems) = await PageIssuesAsync(query, i => i.PainVelocity);
                break;
            default:
                (issues, hasMore, nextCursor, totalItems) = await PageIssuesAsync(query, i => i.PainScore);
                break;
        }

        async Task<(List<Domain.Entities.Issue>, bool, string?, int)> PageIssuesAsync<TKey>(IQueryable<Domain.Entities.Issue> q, System.Linq.Expressions.Expression<Func<Domain.Entities.Issue, TKey>> key)
        {
            var total = seeking && KeysetPager.TryDecodeCursor<TKey>(cursor, out _, out _) ? -1 : await q.CountAsync();
            var p = await q.ToKeysetPageAsync(key, i => i.Id, true, true, cursor, pageSize, skip: skip);
            return (p.Items, p.HasMore, p.NextCursor, total);
        }

        // Get comment counts
        var issueIds = issues.Select(i => i.Id).ToList();
        var commentCounts = await _context.IssueComments
            .Where(c => issueIds.Contains(c.IssueId))
            .GroupBy(c => c.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Get display names
        var creatorIds = issues.Select(i => i.CreatorUserId).Distinct().ToList();
        var displayNames = await GetDisplayNames(creatorIds);

        // Get user votes if logged in
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var userVotes = userId != null
            ? await _context.IssueVotes.Where(v => v.UserId == userId && issueIds.Contains(v.IssueId)).ToListAsync()
            : new List<IssueVote>();

        var dtos = issues.Select(i => new IssueDto
        {
            Id = i.Id,
            Title = i.Title,
            Description = i.Description,
            Status = i.Status.ToString(),
            Type = i.Type.ToString(),
            IsClosed = i.IsClosed,
            PainScore = i.PainScore,
            PainVelocity = i.PainVelocity,
            CategoryName = i.Category?.Name ?? "Uncategorized",
            SubcategoryName = i.Subcategory?.Name ?? "",
            Severity = i.Severity.ToString(),
            Frequency = i.Frequency.ToString(),
            ImpactsMoney = i.ImpactsMoney,
            FinancialImpactAmount = i.FinancialImpactAmount,
            CreatedAt = i.CreatedAt,
            UpdatedAt = i.UpdatedAt,
            Votes = i.Votes,
            CommentCount = commentCounts.FirstOrDefault(cc => cc.IssueId == i.Id)?.Count ?? 0,
            CreatorId = i.CreatorUserId,
            CreatorName = i.CreatorUserId != null && displayNames.ContainsKey(i.CreatorUserId) ? displayNames[i.CreatorUserId] : "Anonymous",
            UserVote = userVotes.FirstOrDefault(v => v.IssueId == i.Id)?.Value ?? 0,
            Labels = i.Labels.Select(la => new IssueLabelDto { Id = la.LabelId, Name = la.Label?.Name ?? "", Color = la.Label?.Color ?? "#6366f1" }).ToList(),
            GitHubIssueUrl = i.GitHubIssueUrl
        }).ToList();

        return Ok(new { data = dtos, totalItems, page, pageSize, hasMore, nextCursor });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetIssueDetail(int id)
    {
        var issue = await _context.Issues
            .Include(i => i.Category)
            .Include(i => i.Subcategory)
            .Include(i => i.Labels).ThenInclude(la => la.Label)
            .Include(i => i.Assignees)
            .Include(i => i.Milestone)
            .Include(i => i.StatusHistory)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (issue == null) return NotFound();

        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var userVote = userId != null ? await _context.IssueVotes.FirstOrDefaultAsync(v => v.IssueId == id && v.UserId == userId) : null;
        var commentCount = await _context.IssueComments.CountAsync(c => c.IssueId == id);
        var creatorName = await GetDisplayName(issue.CreatorUserId);
        var closedByName = issue.IsClosed ? await GetDisplayName(issue.ClosedByUserId) : null;

        // Resolve assignee names
        var assigneeIds = issue.Assignees.Select(a => a.UserId).ToList();
        var assigneeNames = await GetDisplayNames(assigneeIds);
        var assignees = issue.Assignees.Select(a => new { userId = a.UserId, displayName = assigneeNames.GetValueOrDefault(a.UserId, "Unknown") }).ToList();

        var labels = issue.Labels.Select(la => new { id = la.LabelId, name = la.Label?.Name ?? "", color = la.Label?.Color ?? "#6366f1" }).ToList();

        var statusHistory = new List<object>();
        if (issue.StatusHistory != null)
        {
            var historyList = issue.StatusHistory.OrderBy(h => h.ChangedAt).ToList();
            var changerIds = historyList.Select(h => h.ChangedByUserId).Where(uid => uid != null).Distinct().ToList();
            var changerNames = await GetDisplayNames(changerIds);

            foreach (var h in historyList)
            {
                statusHistory.Add(new
                {
                    id = h.Id,
                    oldStatus = h.OldStatus.ToString(),
                    newStatus = h.NewStatus.ToString(),
                    changedByUserId = h.ChangedByUserId,
                    changedByName = h.ChangedByUserId != null && changerNames.ContainsKey(h.ChangedByUserId)
                        ? changerNames[h.ChangedByUserId]
                        : "System",
                    changedAt = h.ChangedAt
                });
            }
        }

        // Get allowed transitions for current user
        var isAdmin = User.IsInRole("Admin");
        var allowedTransitions = _workflowService.GetAllowedTransitions(issue.Status, isAdmin)
            .Select(s => s.ToString())
            .ToList();

        return Ok(new
        {
            id = issue.Id,
            title = issue.Title,
            description = issue.Description,
            status = issue.Status.ToString(),
            type = issue.Type.ToString(),
            isClosed = issue.IsClosed,
            closedAt = issue.ClosedAt,
            closedByName,
            painScore = issue.PainScore,
            painVelocity = issue.PainVelocity,
            categoryName = issue.Category?.Name ?? "Uncategorized",
            subcategoryName = issue.Subcategory?.Name ?? "",
            severity = issue.Severity.ToString(),
            frequency = issue.Frequency.ToString(),
            impactsMoney = issue.ImpactsMoney,
            financialImpactAmount = issue.FinancialImpactAmount,
            createdAt = issue.CreatedAt,
            updatedAt = issue.UpdatedAt,
            votes = issue.Votes,
            creatorId = issue.CreatorUserId,
            creatorName,
            userVote = userVote?.Value ?? 0,
            commentCount,
            labels,
            assignees,
            milestoneId = issue.MilestoneId,
            milestoneTitle = issue.Milestone?.Title,
            gitHubIssueUrl = issue.GitHubIssueUrl,
            statusHistory,
            allowedTransitions
        });
    }

    [HttpPost("check-similar")]
    public async Task<ActionResult<List<IssueDto>>> CheckSimilar([FromBody] CreateIssueDto input)
    {
        var similar = await _similarityService.FindSimilarIssuesAsync(input.Title, input.Description);
        return Ok(similar.Select(i => new IssueDto { 
            Id = i.Id, 
            Title = i.Title, 
            Description = i.Description,
            Status = i.Status.ToString(),
            PainScore = i.PainScore,
            CategoryName = i.Category?.Name ?? "Similar Match", 
            CreatedAt = i.CreatedAt
        }).ToList());
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<int>> Create([FromBody] CreateIssueDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issueType = Enum.TryParse<IssueType>(input.Type, true, out var parsed) ? parsed : IssueType.Bug;
        var severity = Enum.TryParse<IssueSeverity>(input.Severity, true, out var sev) ? sev : IssueSeverity.Minor;
        var frequency = Enum.TryParse<IssueFrequency>(input.Frequency, true, out var freq) ? freq : IssueFrequency.Rare;
        
        var issue = new Domain.Entities.Issue
        {
            Title = input.Title,
            Description = Application.Common.Helpers.HtmlSanitizer.Clean(input.Description),
            Priority = Enum.TryParse<IssuePriority>(input.Priority, true, out var pri) ? pri : IssuePriority.Medium,
            Type = issueType,
            CategoryId = input.CategoryId,
            SubcategoryId = input.SubcategoryId,
            Severity = severity,
            ImpactsMoney = input.ImpactsMoney,
            FinancialImpactAmount = input.FinancialImpactAmount,
            Frequency = frequency,
            Status = IssueStatus.New,
            CreatorUserId = userId,
            GitHubIssueUrl = input.GitHubIssueUrl
        };
        
        issue.PainScore = _rankingService.CalculatePainScore(issue);

        _context.Issues.Add(issue);
        await _context.SaveChangesAsync();

        // Log IssueCreated activity
        await _activityService.LogActivityAsync(issue.Id, userId ?? "System", "IssueCreated", $"Issue created: \"{issue.Title}\"");

        // Create initial status history log
        var initialHistory = new IssueStatusHistory
        {
            IssueId = issue.Id,
            OldStatus = IssueStatus.New,
            NewStatus = IssueStatus.New,
            ChangedByUserId = userId ?? "System",
            ChangedAt = DateTime.UtcNow
        };
        _context.IssueStatusHistories.Add(initialHistory);
        await _context.SaveChangesAsync();

        // Trigger karma recalculation so the user appears on the leaderboard
        if (userId != null)
        {
            await _gamificationService.RecalculateKarma(userId);
        }

        return Ok(issue.Id);
    }

    /// <summary>
    /// The quick bug report button: creates a normal Feedback Hub issue and keeps the screenshot and technical context
    /// (page, recent errors, API calls) with it, so the problem can be understood without asking the user again.
    /// </summary>
    [HttpPost("quick-report")]
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("bulk")]
    public async Task<ActionResult> QuickReport([FromBody] Application.DTOs.Issue.QuickReportDto input)
    {
        static ActionResult Fail(string message) => new OkObjectResult(new { isSuccess = false, error = new { code = "QuickReport.Invalid", description = message } });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var text = (input.Description ?? string.Empty).Trim();
        if (text.Length < 3) return Fail("Please describe the problem in a few words.");
        if (text.Length > 4000) return Fail("The description is too long. Please keep it under 4000 characters.");
        var context = input.ContextJson ?? "{}";
        if (context.Length > 200_000) return Fail("The technical details are too large to send.");

        byte[]? shot = null;
        string? shotType = null;
        if (!string.IsNullOrWhiteSpace(input.ScreenshotBase64))
        {
            var b64 = input.ScreenshotBase64;
            var comma = b64.IndexOf(',');
            if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) b64 = b64[(comma + 1)..];
            if (b64.Length > 3_000_000) return Fail("The screenshot is too large.");
            try { shot = Convert.FromBase64String(b64); } catch (FormatException) { return Fail("The screenshot could not be read."); }
            if (shot.Length > 2_200_000) return Fail("The screenshot is too large.");
            if (shot.Length > 3 && shot[0] == 0xFF && shot[1] == 0xD8) shotType = "image/jpeg";
            else if (shot.Length > 4 && shot[0] == 0x89 && shot[1] == 0x50 && shot[2] == 0x4E && shot[3] == 0x47) shotType = "image/png";
            else return Fail("The screenshot must be a JPEG or PNG image.");
        }

        var firstLine = Application.Common.Helpers.HtmlSanitizer.Clean(text.Split((char)10, 2)[0]).Trim();
        var title = "Bug: " + (firstLine.Length > 80 ? firstLine[..80].TrimEnd() + "…" : firstLine);
        var created = await Create(new CreateIssueDto
        {
            Title = title,
            Description = text,
            Type = "Bug",
            Severity = input.Severity ?? "Minor",
            ImpactsMoney = input.ImpactsMoney ?? false
        });
        if (created.Result is not OkObjectResult ok || ok.Value is not int issueId)
            return Fail("The report could not be saved. Please try again.");

        var fingerprint = Application.Common.Helpers.ReportFingerprint.From(context);
        _context.IssueDiagnostics.Add(new IssueDiagnostic
        {
            IssueId = issueId,
            UserId = userId,
            ContextJson = context,
            Fingerprint = fingerprint,
            Screenshot = shot,
            ScreenshotContentType = shotType
        });
        await _context.SaveChangesAsync();

        // How many other people already reported the same thing (same page, error and failing call).
        var similar = fingerprint == null ? 0 : await _context.IssueDiagnostics
            .Where(d => d.Fingerprint == fingerprint && d.UserId != userId).Select(d => d.UserId).Distinct().CountAsync();

        return Ok(new { isSuccess = true, value = new { id = issueId, similarReports = similar } });
    }

    /// <summary>
    /// The signed-in person's own reports, newest first, with their status. Paged by keyset: pass the previous page's
    /// nextCursor as ?cursor= (served from IX_Issues_CreatorUserId_CreatedAt_Id_Live, so page 50 costs the same as page 2).
    /// </summary>
    [HttpGet("my-reports")]
    [Authorize]
    public async Task<ActionResult> GetMyReports([FromQuery] string? cursor = null, [FromQuery] int pageSize = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.Issues.Where(i => i.CreatorUserId == userId);
        var total = -1;
        if (Application.Common.Models.Keyset.TryDecode(cursor, out var afterDate, out var afterId))
            query = query.After(i => i.CreatedAt, i => i.Id, afterDate, afterId);
        else
            total = await query.CountAsync(); // only the first page counts

        var rows = await query
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Take(pageSize + 1)
            .Select(i => new
            {
                i.Id, i.Title, i.Status, i.Severity, i.IsClosed, i.CreatedAt,
                Diag = _context.IssueDiagnostics.Where(d => d.IssueId == i.Id)
                    .Select(d => new { d.Fingerprint, HasShot = d.Screenshot != null }).FirstOrDefault()
            })
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        var fingerprints = page.Where(r => r.Diag?.Fingerprint != null).Select(r => r.Diag!.Fingerprint!).Distinct().ToList();
        var reporters = fingerprints.Count == 0
            ? new Dictionary<string, int>()
            : await _context.IssueDiagnostics.Where(d => d.Fingerprint != null && fingerprints.Contains(d.Fingerprint))
                .GroupBy(d => d.Fingerprint!).Select(g => new { g.Key, Users = g.Select(x => x.UserId).Distinct().Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Users);

        var data = page.Select(r => new Application.DTOs.Issue.MyReportDto
        {
            Id = r.Id,
            Title = r.Title,
            Status = r.Status.ToString(),
            Severity = r.Severity.ToString(),
            IsClosed = r.IsClosed,
            CreatedAt = r.CreatedAt,
            HasDiagnostics = r.Diag != null,
            HasScreenshot = r.Diag?.HasShot ?? false,
            SimilarReports = r.Diag?.Fingerprint != null && reporters.TryGetValue(r.Diag.Fingerprint, out var users) ? Math.Max(0, users - 1) : 0
        }).ToList();

        var result = new Application.Common.Models.PaginatedResult<Application.DTOs.Issue.MyReportDto>(data, total, 1, pageSize)
        {
            HasMore = hasMore,
            NextCursor = hasMore && page.Count > 0 ? Application.Common.Models.Keyset.Encode(page[^1].CreatedAt, page[^1].Id) : null
        };
        return Ok(new { isSuccess = true, value = result });
    }

    /// <summary>For the team: the problems reported most often, grouped by fingerprint, with one example issue each.</summary>
    [HttpGet("report-groups")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetReportGroups([FromQuery] int top = 20)
    {
        top = Math.Clamp(top, 1, 100);
        var groups = await _context.IssueDiagnostics.Where(d => d.Fingerprint != null)
            .GroupBy(d => d.Fingerprint!)
            .Select(g => new { Fingerprint = g.Key, Reports = g.Count(), Reporters = g.Select(x => x.UserId).Distinct().Count(), ExampleIssueId = g.Min(x => x.IssueId), Latest = g.Max(x => x.CreatedAt) })
            .OrderByDescending(g => g.Reporters).ThenByDescending(g => g.Reports).Take(top).ToListAsync();
        return Ok(new { isSuccess = true, value = groups });
    }

    /// <summary>The technical context saved with a quick report. Only the reporter and admins can read it.</summary>
    [HttpGet("{id}/diagnostics")]
    [Authorize]
    public async Task<ActionResult> GetDiagnostics(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var d = await _context.IssueDiagnostics.Where(x => x.IssueId == id).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        // No report details (a normal issue) or not yours to see: an empty result, not an error, so the page shows nothing.
        if (d == null || (d.UserId != userId && !User.IsInRole("Admin"))) return Ok(new { isSuccess = true, value = (object?)null });
        return Ok(new { isSuccess = true, value = new { contextJson = d.ContextJson, hasScreenshot = d.Screenshot != null, createdAt = d.CreatedAt } });
    }

    [HttpGet("{id}/diagnostics/screenshot")]
    [Authorize]
    public async Task<ActionResult> GetDiagnosticsScreenshot(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var d = await _context.IssueDiagnostics.Where(x => x.IssueId == id && x.Screenshot != null).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (d == null) return NotFound();
        if (d.UserId != userId && !User.IsInRole("Admin")) return Forbid();
        return File(d.Screenshot!, d.ScreenshotContentType ?? "image/jpeg");
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult> UpdateIssue(int id, [FromBody] UpdateIssueDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");

        // 1. Only creator or Admin can edit
        if (issue.CreatorUserId != userId && !isAdmin)
            return Ok(new { success = false, message = "Only the issue creator or an administrator can edit this issue." });

        // 2. If the issue is already in a roadmap status, only Admins can edit it
        var roadmapStatuses = new[] { IssueStatus.Planned, IssueStatus.InProgress, IssueStatus.Released };
        if (roadmapStatuses.Contains(issue.Status) && !isAdmin)
            return Ok(new { success = false, message = "Only administrators can edit active roadmap items." });

        if (input.Title != null) issue.Title = input.Title;
        if (input.Description != null) issue.Description = Application.Common.Helpers.HtmlSanitizer.Clean(input.Description);
        if (input.Type != null && Enum.TryParse<IssueType>(input.Type, true, out var t)) issue.Type = t;
        if (input.CategoryId.HasValue) issue.CategoryId = input.CategoryId;
        if (input.SubcategoryId.HasValue) issue.SubcategoryId = input.SubcategoryId;
        if (input.Severity != null && Enum.TryParse<IssueSeverity>(input.Severity, true, out var s)) issue.Severity = s;
        if (input.Frequency != null && Enum.TryParse<IssueFrequency>(input.Frequency, true, out var f)) issue.Frequency = f;
        if (input.ImpactsMoney.HasValue) issue.ImpactsMoney = input.ImpactsMoney.Value;
        if (input.FinancialImpactAmount.HasValue) issue.FinancialImpactAmount = input.FinancialImpactAmount;

        issue.PainScore = _rankingService.CalculatePainScore(issue);
        await _context.SaveChangesAsync();

        // Log IssueUpdated activity
        await _activityService.LogActivityAsync(issue.Id, userId ?? "System", "IssueUpdated", "Updated issue details");

        return Ok(new { success = true, message = "Issue updated successfully." });
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    [HttpPut("{id}/status")]
    [Authorize]
    public async Task<ActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto input)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        if (!Enum.TryParse<IssueStatus>(input.Status, true, out var newStatus))
            return BadRequest(new { message = "Invalid status value." });

        var isAdmin = User.IsInRole("Admin");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        if (!isAdmin && issue.CreatorUserId != userId) return Forbid();

        var (success, message) = await _workflowService.TransitionAsync(issue, newStatus, userId, isAdmin);

        if (!success)
            return Ok(new { success, message });

        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        return Ok(new { success = true, message, status = issue.Status.ToString() });
    }

    // ==================== CLOSE / REOPEN ====================

    [HttpPost("{id}/close")]
    [Authorize]
    public async Task<ActionResult> CloseIssue(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && issue.CreatorUserId != userId) return Forbid();
        var (success, message) = await _workflowService.CloseAsync(issue, userId, isAdmin);

        if (!success)
            return Ok(new { success, message });

        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        var closedByName = await GetDisplayName(userId);
        return Ok(new { success = true, message, isClosed = true, closedAt = issue.ClosedAt, closedByName });
    }

    [HttpPost("{id}/reopen")]
    [Authorize]
    public async Task<ActionResult> ReopenIssue(int id)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        if (!isAdmin && issue.CreatorUserId != userId) return Forbid();
        var (success, message) = await _workflowService.ReopenAsync(issue, userId, isAdmin);

        if (!success)
            return Ok(new { success, message });

        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        return Ok(new { success = true, message, isClosed = false });
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<ActionResult> DeleteIssue(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");

        // Only creator or Admin can delete
        if (issue.CreatorUserId != userId && !isAdmin)
            return Ok(new { success = false, message = "Only the issue creator or an administrator can delete this issue." });

        issue.IsDeleted = true;
        issue.DeletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Log IssueDeleted activity
        await _activityService.LogActivityAsync(issue.Id, userId ?? "System", "IssueDeleted", "Soft deleted the issue");

        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        return Ok(new { success = true, message = "Issue deleted successfully." });
    }

    // ==================== ASSIGNEES ====================

    [HttpPost("{id}/assignees")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> AddAssignee(int id, [FromBody] AssigneeDto input)
    {
        if (!await _context.Issues.AnyAsync(i => i.Id == id)) return NotFound();
        if (await _userManager.FindByIdAsync(input.UserId) == null) return BadRequest(new { message = "User not found." });
        var exists = await _context.IssueAssignees.AnyAsync(a => a.IssueId == id && a.UserId == input.UserId);
        if (exists) return Ok(new { success = true, message = "Already assigned." });
        _context.IssueAssignees.Add(new IssueAssignee { IssueId = id, UserId = input.UserId });
        await _context.SaveChangesAsync();
        var name = await GetDisplayName(input.UserId);

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        await _activityService.LogActivityAsync(id, currentUserId, "AssigneeAdded", $"Assigned to {name}", new { assigneeUserId = input.UserId, assigneeName = name });

        return Ok(new { success = true, message = "Assignee added.", userId = input.UserId, displayName = name });
    }

    [HttpDelete("{id}/assignees/{assigneeUserId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> RemoveAssignee(int id, string assigneeUserId)
    {
        var a = await _context.IssueAssignees.FirstOrDefaultAsync(a => a.IssueId == id && a.UserId == assigneeUserId);
        if (a == null) return NotFound();
        _context.IssueAssignees.Remove(a);
        await _context.SaveChangesAsync();

        var name = await GetDisplayName(assigneeUserId);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        await _activityService.LogActivityAsync(id, currentUserId, "AssigneeRemoved", $"Unassigned {name}", new { assigneeUserId = assigneeUserId, assigneeName = name });

        return Ok(new { success = true, message = "Assignee removed." });
    }

    // ==================== MILESTONES ====================

    [HttpGet("milestones")]
    [AllowAnonymous]
    public async Task<ActionResult> GetMilestones()
    {
        var milestones = await _context.IssueMilestones.Include(m => m.Issues).OrderByDescending(m => m.CreatedAt).ToListAsync();
        return Ok(milestones.Select(m => new MilestoneDto
        {
            Id = m.Id, Title = m.Title, Description = m.Description, DueDate = m.DueDate, IsClosed = m.IsClosed,
            OpenCount = m.Issues.Count(i => !i.IsClosed), ClosedCount = m.Issues.Count(i => i.IsClosed),
            Progress = m.Issues.Count > 0 ? Math.Round(m.Issues.Count(i => i.IsClosed) * 100.0 / m.Issues.Count, 1) : 0
        }));
    }

    [HttpPost("milestones")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> CreateMilestone([FromBody] CreateMilestoneDto input)
    {
        var milestone = new IssueMilestone { Title = input.Title, Description = input.Description, DueDate = input.DueDate };
        _context.IssueMilestones.Add(milestone);
        await _context.SaveChangesAsync();
        return Ok(new MilestoneDto { Id = milestone.Id, Title = milestone.Title, Description = milestone.Description, DueDate = milestone.DueDate });
    }

    // ==================== TAXONOMY ====================

    [HttpGet("taxonomy")]
    [AllowAnonymous]
    public async Task<ActionResult<List<IssueTaxonomy>>> GetTaxonomies()
    {
        var list = await _context.IssueTaxonomies
            .Include(t => t.Children)
            .Where(t => t.ParentId == null) // Root categories
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("taxonomy")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IssueTaxonomy>> CreateTaxonomy([FromBody] IssueTaxonomy input)
    {
        var taxonomy = new IssueTaxonomy
        {
            Name = input.Name,
            Type = input.ParentId == null ? "Category" : "Subcategory",
            ParentId = input.ParentId
        };
        _context.IssueTaxonomies.Add(taxonomy);
        await _context.SaveChangesAsync();
        return Ok(taxonomy);
    }

    [HttpPost("seed")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> SeedTaxonomy([FromServices] TaxonomySeederService seeder)
    {
        await seeder.SeedAsync();
        return Ok("Taxonomy seeded.");
    }
}
