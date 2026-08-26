using System;
using System.Collections.Generic;

namespace Application.DTOs.Blog;

public class BlogCommentDto
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public int Likes { get; set; }
    public int? ParentCommentId { get; set; }
    public List<BlogCommentDto> Replies { get; set; } = new();
}

public class CreateBlogCommentDto
{
    public string PostSlug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int? ParentCommentId { get; set; }
}
