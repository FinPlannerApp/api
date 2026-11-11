using Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Sends queued mail. Waits on the in-process queue (no polling, no Redis) and retries a failed
/// send up to 3 times before giving up on that message.
/// </summary>
public class EmailQueueWorker : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
        { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };

    private readonly InMemoryEmailQueue _queue;
    private readonly IDirectEmailSender _directEmailSender;
    private readonly ILogger<EmailQueueWorker> _logger;

    public EmailQueueWorker(
        InMemoryEmailQueue queue,
        IDirectEmailSender directEmailSender,
        ILogger<EmailQueueWorker> logger)
    {
        _queue = queue;
        _directEmailSender = directEmailSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailQueueWorker started (in-memory queue).");

        try
        {
            await foreach (var mail in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
                {
                    try
                    {
                        await _directEmailSender.SendEmailAsync(mail);
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == RetryDelays.Length)
                        {
                            _logger.LogError(ex, "Giving up sending email to {To} after {Attempts} attempts.", mail.To, attempt + 1);
                            break;
                        }

                        _logger.LogWarning(ex, "Sending email to {To} failed (attempt {Attempt}). Retrying.", mail.To, attempt + 1);
                        await Task.Delay(RetryDelays[attempt], stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
