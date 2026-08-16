using Application.Contracts;
using Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

public class DatabaseBlogImageStorage : IBlogImageStorage
{
    private readonly IApplicationDbContext _context;
    private readonly string _apiBaseUrl;

    public DatabaseBlogImageStorage(IApplicationDbContext context, IConfiguration config)
    {
        _context = context;
        _apiBaseUrl = config["ApiBaseUrl"]?.TrimEnd('/') ?? "";
    }

    public async Task<string> StoreAsync(byte[] webpData, string originalFileName)
    {
        var image = new BlogImage
        {
            FileName = originalFileName,
            Data = webpData,
            ContentType = "image/webp",
            SizeBytes = webpData.Length
        };

        _context.BlogImages.Add(image);
        await _context.SaveChangesAsync();

        // Points back at your own API — the image-serving endpoint
        // is what actually streams the bytes when this URL is requested.
        return string.IsNullOrEmpty(_apiBaseUrl)
            ? $"/api/Blog/images/{image.Id}"
            : $"{_apiBaseUrl}/api/Blog/images/{image.Id}";
    }
}
