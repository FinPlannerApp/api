namespace API.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The caller's IP as resolved by the ForwardedHeaders middleware (which honours only the proxies
    /// configured under "ForwardedHeaders"). Never read X-Forwarded-For by hand: any client can send it.
    /// </summary>
    public static string GetClientIp(this HttpContext context)
    {
        // Behind Cloudflare the connection address is a Cloudflare edge server (the 162.158.x.x entries in the device
        // list), not the user. Cloudflare puts the real visitor in CF-Connecting-IP. Only honoured when the deployment
        // says it is really behind Cloudflare, because anyone who can reach the server directly could send that header.
        var config = context.RequestServices.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))
            as Microsoft.Extensions.Configuration.IConfiguration;
        if (config?.GetValue<bool>("ForwardedHeaders:TrustCloudflareHeader") == true)
        {
            var cf = context.Request.Headers["CF-Connecting-IP"].ToString();
            if (System.Net.IPAddress.TryParse(cf, out var parsed)) return parsed.ToString();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
