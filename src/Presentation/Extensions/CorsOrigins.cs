namespace API.Extensions;

/// <summary>The browser origins allowed to call the API. Single source for CORS and the CSRF guard.</summary>
public static class CorsOrigins
{
    public static HashSet<string> Get(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        return new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase)
        {
            "https://finplanner.ska97homelab.uk",
            "http://localhost:4200"
        };
    }
}
