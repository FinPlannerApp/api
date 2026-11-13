namespace Application.Common.Helpers;

/// <summary>Turns a User-Agent string into a short label such as "Chrome on Windows". Display only, never a security check.</summary>
public static class DeviceInfo
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown device";
        var ua = userAgent;

        string browser =
            ua.Contains("Edg/", StringComparison.Ordinal) || ua.Contains("EdgA/", StringComparison.Ordinal) ? "Edge" :
            ua.Contains("OPR/", StringComparison.Ordinal) || ua.Contains("Opera", StringComparison.Ordinal) ? "Opera" :
            ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS", StringComparison.Ordinal) ? "Firefox" :
            ua.Contains("SamsungBrowser", StringComparison.Ordinal) ? "Samsung Internet" :
            ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS", StringComparison.Ordinal) ? "Chrome" :
            ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari" :
            ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase) || ua.Contains("; wv)", StringComparison.Ordinal) ? "App" :
            "Browser";

        string os =
            ua.Contains("Windows", StringComparison.Ordinal) ? "Windows" :
            ua.Contains("Android", StringComparison.Ordinal) ? "Android" :
            ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) || ua.Contains("iPod", StringComparison.Ordinal) ? "iOS" :
            ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal) ? "macOS" :
            ua.Contains("CrOS", StringComparison.Ordinal) ? "ChromeOS" :
            ua.Contains("Linux", StringComparison.Ordinal) ? "Linux" :
            "";

        return os.Length == 0 ? browser : $"{browser} on {os}";
    }
}
