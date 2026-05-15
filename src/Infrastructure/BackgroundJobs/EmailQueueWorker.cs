using Application.DTOs.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

public class EmailQueueWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EmailQueueWorker> _logger;
    private readonly IDirectEmailSender _directEmailSender;
    private const string QueueName = "MailQueue";

    public EmailQueueWorker(
        IConnectionMultiplexer redis, 
        ILogger<EmailQueueWorker> logger, 
        IDirectEmailSender directEmailSender)
    {
        _redis = redis;
        _logger = logger;
        _directEmailSender = directEmailSender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailQueueWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var db = _redis.GetDatabase();
                var message = await db.ListLeftPopAsync(QueueName);

                if (message.HasValue)
                {
                    var mailRequest = JsonSerializer.Deserialize<MailRequest>(message.ToString());
                    if (mailRequest != null)
                    {
                        await _directEmailSender.SendEmailAsync(mailRequest);
                    }
                }
                else
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                _logger.LogWarning("Redis is unavailable. EmailQueueWorker waiting 30s...");
                try { await Task.Delay(30000, stoppingToken); } catch { break; }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EmailQueueWorker.");
                try { await Task.Delay(5000, stoppingToken); } catch { break; }
            }
        }
    }
}
