using Application.Common.Models;
using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Application.Services;

public class BlogImageService
{
    private readonly IApplicationDbContext _context;

    public BlogImageService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<(string ContentType, byte[] Data)>> GetImageAsync(int id)
    {
        var image = await _context.BlogImages.FirstOrDefaultAsync(img => img.Id == id);
        if (image == null)
            return Result.Failure<(string ContentType, byte[] Data)>(new Error("BlogImage.NotFound", "Image not found."));

        return Result.Success((image.ContentType, image.Data));
    }

    public async Task<Result<(int Id, string PublicUrl)>> UploadAndCompressAsync(string fileName, Stream stream)
    {
        try
        {
            using var image = await Image.LoadAsync(stream);

            int maxWidth = 1200;
            if (image.Width > maxWidth)
            {
                int newHeight = (int)((double)image.Height / image.Width * maxWidth);
                image.Mutate(x => x.Resize(maxWidth, newHeight));
            }

            using var ms = new MemoryStream();
            var encoder = new WebpEncoder { Quality = 80 };
            await image.SaveAsync(ms, encoder);

            var webpBytes = ms.ToArray();
            var cleanFileName = Path.GetFileNameWithoutExtension(fileName) + ".webp";

            var blogImage = new BlogImage
            {
                FileName = cleanFileName,
                ContentType = "image/webp",
                Data = webpBytes,
                FileSize = webpBytes.Length
            };

            _context.BlogImages.Add(blogImage);
            await _context.SaveChangesAsync();

            return Result.Success((blogImage.Id, $"/api/Blog/images/{blogImage.Id}"));
        }
        catch (Exception ex)
        {
            return Result.Failure<(int Id, string PublicUrl)>(new Error("BlogImage.UploadFailed", ex.Message));
        }
    }
}
