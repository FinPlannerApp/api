using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

[Table("BlogPostComments", Schema = "accounts")]
public class BlogPostComment
{
    public int Id { get; set; }
    public int BlogPostId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int? ParentCommentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int LikesCount { get; set; } = 0;
}
