using System.Text.RegularExpressions;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Attachments;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface IAttachmentService
{
    Task<Result<List<AttachmentDto>>> ListAsync(string userId, string entityType, int entityId);
    Task<Result<AttachmentDto>> AddAsync(string userId, AddAttachmentDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
}

/// <summary>Document references (links or Drive file ids) for policies, transactions and split expenses. Files are never stored or fetched here.</summary>
public partial class AttachmentService : IAttachmentService
{
    private readonly IApplicationDbContext _context;
    private readonly ISplitService _split;
    public AttachmentService(IApplicationDbContext context, ISplitService split) { _context = context; _split = split; }

    private const int MaxPerEntity = 20;
    private const int MaxPerUser = 500;

    // Exact hosts, plus a few whose subdomains are legitimate (tenant sites). Anything else is refused.
    private static readonly string[] ExactHosts =
    {
        "drive.google.com", "docs.google.com", "www.dropbox.com", "dropbox.com", "1drv.ms", "onedrive.live.com",
        "www.icloud.com", "icloud.com", "app.box.com",
    };
    private static readonly string[] SuffixHosts = { ".sharepoint.com", ".box.com" };

    [GeneratedRegex("^[A-Za-z0-9_-]{10,100}$")]
    private static partial Regex DriveIdRegex();

    public static bool TryNormaliseLink(string? raw, out string url, out string host, out string error)
    {
        url = host = error = "";
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length > 2000) { error = "Paste a share link (up to 2000 characters)."; return false; }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        { error = "Only secure https links are accepted."; return false; }
        if (!string.IsNullOrEmpty(uri.UserInfo)) { error = "That link contains a username or password, which is not allowed."; return false; }
        var h = uri.IdnHost.ToLowerInvariant();
        var ok = ExactHosts.Contains(h) || SuffixHosts.Any(s => h.EndsWith(s, StringComparison.Ordinal) && h.Length > s.Length);
        if (!ok)
        { error = "Only Google Drive, Dropbox, OneDrive, SharePoint, iCloud and Box links are accepted."; return false; }
        url = uri.GetLeftPart(UriPartial.Path) + uri.Query;   // drop any #fragment
        host = h;
        return true;
    }

    private static string DriveUrl(string fileId) => $"https://drive.google.com/file/d/{fileId}/view";

    private async Task<Result<bool>> CanAccessAsync(string userId, string entityType, int entityId)
    {
        switch (entityType)
        {
            case "InsurancePolicy":
                return await _context.InsurancePolicies.AnyAsync(p => p.Id == entityId && p.UserId == userId)
                    ? Result.Success(true) : NotFound();
            case "Transaction":
                return await _context.Transactions.AnyAsync(t => t.Id == entityId && t.UserId == userId)
                    ? Result.Success(true) : NotFound();
            case "SplitExpense":
                var r = await _split.CanAttachReceiptAsync(userId, entityId);
                return r.IsSuccess ? r : NotFound();
            default:
                return Result.Failure<bool>(new Error("Attachment.EntityType", "You can't attach a document to that."));
        }
    }
    private static Result<bool> NotFound() => Result.Failure<bool>(new Error("Attachment.NotFound", "Item not found."));

    private static AttachmentDto Map(Attachment a, string userId)
    {
        var url = a.Provider == (int)AttachmentProvider.GoogleDrive && a.ExternalId != null ? DriveUrl(a.ExternalId) : a.Url ?? "";
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.IdnHost : "";
        return new AttachmentDto
        {
            Id = a.Id, EntityType = a.EntityType, EntityId = a.EntityId, Provider = a.Provider, Url = url, Host = host,
            DisplayName = a.DisplayName, MimeType = a.MimeType, SizeBytes = a.SizeBytes, AddedByMe = a.UserId == userId, CreatedAt = a.CreatedAt,
        };
    }

    public async Task<Result<List<AttachmentDto>>> ListAsync(string userId, string entityType, int entityId)
    {
        var access = await CanAccessAsync(userId, entityType, entityId);
        if (access.IsFailure) return Result.Failure<List<AttachmentDto>>(access.Error);
        var rows = await _context.Attachments.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId).OrderBy(a => a.CreatedAt).ToListAsync();
        return Result.Success(rows.Select(a => Map(a, userId)).ToList());
    }

    public async Task<Result<AttachmentDto>> AddAsync(string userId, AddAttachmentDto dto)
    {
        var access = await CanAccessAsync(userId, dto.EntityType ?? "", dto.EntityId);
        if (access.IsFailure) return Result.Failure<AttachmentDto>(access.Error);

        var name = (dto.DisplayName ?? "").Trim();
        if (name.Length == 0) return Result.Failure<AttachmentDto>(new Error("Attachment.Name", "Give the document a short name."));
        if (name.Length > 120) name = name[..120];

        if (await _context.Attachments.CountAsync(a => a.EntityType == dto.EntityType && a.EntityId == dto.EntityId) >= MaxPerEntity)
            return Result.Failure<AttachmentDto>(new Error("Attachment.Limit", $"Up to {MaxPerEntity} documents per item."));
        if (await _context.Attachments.CountAsync(a => a.UserId == userId) >= MaxPerUser)
            return Result.Failure<AttachmentDto>(new Error("Attachment.Limit", "You have reached the document limit."));

        var a = new Attachment { UserId = userId, EntityType = dto.EntityType!, EntityId = dto.EntityId, DisplayName = name, Provider = dto.Provider };
        if (dto.Provider == (int)AttachmentProvider.Link)
        {
            if (!TryNormaliseLink(dto.Url, out var url, out _, out var err))
                return Result.Failure<AttachmentDto>(new Error("Attachment.Link", err));
            a.Url = url;
        }
        else if (dto.Provider == (int)AttachmentProvider.GoogleDrive)
        {
            if (string.IsNullOrEmpty(dto.ExternalId) || !DriveIdRegex().IsMatch(dto.ExternalId))
                return Result.Failure<AttachmentDto>(new Error("Attachment.DriveId", "That Drive file reference is not valid."));
            a.ExternalId = dto.ExternalId;
            a.MimeType = dto.MimeType is { Length: <= 100 } m ? m : null;
            a.SizeBytes = dto.SizeBytes is >= 0 ? dto.SizeBytes : null;
        }
        else return Result.Failure<AttachmentDto>(new Error("Attachment.Provider", "Unknown document source."));

        _context.Attachments.Add(a);
        var save = await Application.Common.Helpers.ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return Result.Failure<AttachmentDto>(save.Error);
        return Result.Success(Map(a, userId));
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        // Only whoever added the reference can remove it. Removing it never touches the file itself.
        var a = await _context.Attachments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (a == null) return NotFound();
        a.IsDeleted = true; a.DeletedAt = DateTime.UtcNow;
        return await Application.Common.Helpers.ConcurrencySafeSave.TrySaveChangesAsync(_context);
    }
}
