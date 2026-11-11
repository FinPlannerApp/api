using Application.Contracts;
using Application.DTOs.Auth;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>Queues mail for the background worker so requests never wait on the SMTP/API call.</summary>
public class QueuedEmailService : IEmailService
{
    private readonly InMemoryEmailQueue _queue;
    private readonly IDirectEmailSender _directEmailSender;
    private readonly ILogger<QueuedEmailService> _logger;

    public QueuedEmailService(
        InMemoryEmailQueue queue,
        IDirectEmailSender directEmailSender,
        ILogger<QueuedEmailService> logger)
    {
        _queue = queue;
        _directEmailSender = directEmailSender;
        _logger = logger;
    }

    public async Task SendEmailAsync(MailRequest mailRequest)
    {
        if (_queue.TryEnqueue(mailRequest))
        {
            _logger.LogInformation("Email to {To} queued.", mailRequest.To);
            return;
        }

        _logger.LogWarning("Email queue is full. Sending directly to {To}.", mailRequest.To);
        await _directEmailSender.SendEmailAsync(mailRequest);
    }
}
