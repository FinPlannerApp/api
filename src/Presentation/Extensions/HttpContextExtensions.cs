namespace API.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The caller's IP as resolved by the ForwardedHeaders middleware (which honours only the proxies
    /// configured under "ForwardedHeaders"). Never read X-Forwarded-For by hand: any client can send it.
    /// </summary>
    public static string GetClientIp(this HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
