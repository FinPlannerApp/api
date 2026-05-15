using Application.Contracts;
using Application.DTOs.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure.Services;

public class RedisEmailService : IEmailService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisEmailService> _logger;
    private readonly IDirectEmailSender _directEmailSender;
    private const string QueueName = "MailQueue";

    public RedisEmailService(
        IConnectionMultiplexer redis, 
        ILogger<RedisEmailService> logger,
        IDirectEmailSender directEmailSender)
    {
        _redis = redis;
        _logger = logger;
        _directEmailSender = directEmailSender;
    }

    public async Task SendEmailAsync(MailRequest mailRequest)
    {
        var json = JsonSerializer.Serialize(mailRequest);
        try 
        {
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(QueueName, json);
            _logger.LogInformation("Email to {To} queued into Redis.", mailRequest.To);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or ObjectDisposedException)
        {
            _logger.LogWarning("Redis is unavailable. Falling back to direct email sending for {To}.", mailRequest.To);
            await _directEmailSender.SendEmailAsync(mailRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error queuing email for {To}. Falling back to direct email.", mailRequest.To);
            await _directEmailSender.SendEmailAsync(mailRequest);
        }
    }
}
