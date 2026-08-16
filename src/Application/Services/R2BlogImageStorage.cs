using Amazon.S3;
using Amazon.S3.Model;
using Application.Contracts;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

/// <summary>
/// Cloudflare R2 implementation of IBlogImageStorage.
/// When switching from database storage to R2, swap the DI registration
/// of IBlogImageStorage to use this implementation instead.
/// </summary>
public class R2BlogImageStorage : IBlogImageStorage
{
    private readonly IConfiguration _config;

    public R2BlogImageStorage(IConfiguration config)
    {
        _config = config;
    }

    public async Task<string> StoreAsync(byte[] webpData, string originalFileName)
    {
        var serviceUrl = _config["R2:ServiceUrl"] ?? throw new InvalidOperationException("R2:ServiceUrl configuration is missing.");
        var accessKey = _config["R2:AccessKeyId"] ?? throw new InvalidOperationException("R2:AccessKeyId configuration is missing.");
        var secretKey = _config["R2:SecretAccessKey"] ?? throw new InvalidOperationException("R2:SecretAccessKey configuration is missing.");
        var bucketName = _config["R2:BucketName"] ?? throw new InvalidOperationException("R2:BucketName configuration is missing.");
        var publicUrl = _config["R2:PublicUrl"]?.TrimEnd('/') ?? throw new InvalidOperationException("R2:PublicUrl configuration is missing.");

        var s3Config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true
        };

        using var client = new AmazonS3Client(accessKey, secretKey, s3Config);
        
        var cleanFileName = Path.GetFileNameWithoutExtension(originalFileName);
        var objectKey = $"blog/{Guid.NewGuid()}-{cleanFileName}.webp";

        using var ms = new MemoryStream(webpData);
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = ms,
            ContentType = "image/webp"
        };

        await client.PutObjectAsync(putRequest);

        return $"{publicUrl}/{objectKey}";
    }
}
