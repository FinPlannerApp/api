using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Application.Common.Helpers;

/// <summary>
/// A short, stable fingerprint of a quick bug report so the same problem reported by different people can be grouped:
/// the page, the first console error and the first failed API call, with ids, numbers and times removed so they do not
/// make every report look different. Null when the report carries neither an error nor a failed call (nothing to match on).
/// </summary>
public static class ReportFingerprint
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"\b[0-9a-f]{8,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Clock = new(@"^\d{2}:\d{2}:\d{2}\s+", RegexOptions.Compiled);

    public static string? From(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            var root = doc.RootElement;
            var page = Normalize(Str(root, "page"));

            string? error = null;
            if (root.TryGetProperty("consoleErrors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                error = Normalize(Clock.Replace(errors[0].GetString() ?? string.Empty, string.Empty));

            string? call = null;
            if (root.TryGetProperty("failedApiCalls", out var failed) && failed.ValueKind == JsonValueKind.Array && failed.GetArrayLength() > 0)
            {
                var c = failed[0];
                call = $"{Str(c, "method")} {Normalize(Str(c, "path"))} {Str(c, "status")} {Str(c, "error")}";
            }

            if (string.IsNullOrEmpty(error) && string.IsNullOrEmpty(call)) return null;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{page}|{error}|{call}"));
            return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ToString() : string.Empty;

    private static string Normalize(string text) => Digits.Replace(Hex.Replace(text.ToLowerInvariant(), "#"), "#").Trim();
}
