using Application.DTOs.Issue;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues/{issueId}/comments")]
public class CommentsController : ControllerBase
{
    private readonly CommentService _commentService;
    private readonly ReactionService _reactionService;

    public CommentsController(CommentService commentService, ReactionService reactionService)
    {
        _commentService = commentService;
        _reactionService = reactionService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> GetComments(int issueId)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var result = await _commentService.GetCommentsAsync(issueId, userId);
        return Ok(result);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> AddComment(int issueId, [FromBody] CreateCommentDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _commentService.AddCommentAsync(issueId, input, userId);
        return Ok(result);
    }

    [HttpPut("/api/comments/{commentId}")]
    [Authorize]
    public async Task<ActionResult> EditComment(int commentId, [FromBody] UpdateCommentDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (success, message, content, updatedAt) = await _commentService.EditCommentAsync(commentId, input.Content, userId);
        if (!success) return Ok(new { success, message });
        return Ok(new { success, message, content, updatedAt });
    }

    [HttpDelete("/api/comments/{commentId}")]
    [Authorize]
    public async Task<ActionResult> DeleteComment(int commentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (success, message) = await _commentService.DeleteCommentAsync(commentId, userId);
        return Ok(new { success, message });
    }

    [HttpPost("/api/comments/{commentId}/vote")]
    [Authorize]
    public async Task<ActionResult> VoteComment(int commentId, [FromBody] VoteDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (success, message, score, userVote) = await _commentService.VoteCommentAsync(commentId, input.Value, userId);
        return Ok(new { success, message, score, userVote });
    }

    [HttpPost("/api/comments/{commentId}/helpful")]
    [Authorize]
    public async Task<ActionResult> ToggleHelpful(int commentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var isAdmin = User.IsInRole("Admin");
        var (success, message) = await _commentService.ToggleHelpfulAsync(commentId, userId, isAdmin);
        if (!success) return BadRequest(new { message });

        return Ok(new { success = true, message });
    }

    [HttpPost("/api/comments/{commentId}/root-cause")]
    [Authorize]
    public async Task<ActionResult> ToggleRootCause(int commentId)
    {
        var isAdmin = User.IsInRole("Admin");
        var (success, message) = await _commentService.ToggleRootCauseAsync(commentId, isAdmin);
        if (!success) return BadRequest(new { message });

        return Ok(new { success = true, message });
    }

    [HttpPost("/api/comments/{commentId}/repro-confirmed")]
    [Authorize]
    public async Task<ActionResult> ToggleReproConfirmed(int commentId)
    {
        var isAdmin = User.IsInRole("Admin");
        var (success, message) = await _commentService.ToggleReproConfirmedAsync(commentId, isAdmin);
        if (!success) return BadRequest(new { message });

        return Ok(new { success = true, message });
    }

    // ==================== REACTIONS ====================

    [HttpPost("/api/comments/{commentId}/reactions")]
    [Authorize]
    public async Task<ActionResult> ToggleReaction(int commentId, [FromBody] ReactionDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (success, action, error) = await _reactionService.ToggleReactionAsync(commentId, input.Emoji, userId);
        if (!success) return BadRequest(error);
        return Ok(new { success, action });
    }

    [HttpGet("/api/comments/{commentId}/reactions")]
    [AllowAnonymous]
    public async Task<ActionResult> GetReactions(int commentId)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var result = await _reactionService.GetReactionsAsync(commentId, userId);
        return Ok(result);
    }
}
