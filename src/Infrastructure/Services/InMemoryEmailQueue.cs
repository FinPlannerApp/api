using System.Threading.Channels;
using Application.DTOs.Auth;

namespace Infrastructure.Services;

/// <summary>
/// In-process mail queue. Emails used to be queued in Redis and polled every minute, which spent
/// Upstash commands to move a few messages between two parts of the SAME process. Redis is now
/// reserved for OTP codes. A message waiting in this queue is lost only if the app restarts in
/// the few seconds before it is sent (a user can simply request another code).
/// </summary>
public sealed class InMemoryEmailQueue
{
    private readonly Channel<MailRequest> _channel =
        Channel.CreateBounded<MailRequest>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ChannelReader<MailRequest> Reader => _channel.Reader;

    /// <summary>False when the queue is full — the caller should send directly instead.</summary>
    public bool TryEnqueue(MailRequest request) => _channel.Writer.TryWrite(request);
}
