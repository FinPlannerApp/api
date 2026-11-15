using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Automatic valuation. value = units × latest price.
///  • Mutual funds: AMFI's public NAV file (scheme code in "Symbol"), one download for everyone.
///  • Stocks / ETFs: Yahoo Finance's unofficial chart endpoint (ticker in "Symbol", e.g. RELIANCE.NS or 500325.BO).
///    This feed is not an official API and can change or block without notice — failures are recorded on the
///    holding ("LastPriceError") and the previous value is kept; nothing is ever overwritten with a guess.
/// </summary>
public class MarketPriceService : IMarketPriceService
{
    private const string AmfiUrl = "https://www.amfiindia.com/spages/NAVAll.txt";
    private static readonly ConcurrentDictionary<string, DateTime> LastUserRefresh = new();

    private readonly IApplicationDbContext _context;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<MarketPriceService> _logger;

    public MarketPriceService(IApplicationDbContext context, IHttpClientFactory http, ILogger<MarketPriceService> logger)
    { _context = context; _http = http; _logger = logger; }

    public async Task<MarketRefreshResult> RefreshAsync(string? userId, int? accountId, CancellationToken ct)
    {
        // A user can trigger this by hand; keep it from becoming a way to hammer the price sources.
        if (userId != null && accountId == null)
        {
            if (LastUserRefresh.TryGetValue(userId, out var last) && (DateTime.UtcNow - last).TotalSeconds < 60)
                return new MarketRefreshResult(0, 0, "Prices were just refreshed. Try again in a minute.");
            LastUserRefresh[userId] = DateTime.UtcNow;
        }

        var q = _context.InvestmentDetails
            .Include(d => d.Account)
            .Where(d => d.AutoValuation && d.Symbol != null && d.Units > 0 && !d.Account.IsDeleted && !d.Account.IsArchived);
        if (userId != null) q = q.Where(d => d.Account.UserId == userId);
        if (accountId != null) q = q.Where(d => d.AccountId == accountId);
        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return new MarketRefreshResult(0, 0, "No holdings use automatic value.");

        int updated = 0, failed = 0;

        // ── Mutual funds (AMFI) ──
        var funds = rows.Where(r => r.Kind == InvestmentKind.MutualFund).ToList();
        if (funds.Count > 0)
        {
            Dictionary<string, (decimal Nav, DateTime Date)>? navs = null;
            string? err = null;
            try { navs = await DownloadAmfiAsync(funds.Select(f => f.Symbol!.Trim()).ToHashSet(), ct); }
            catch (Exception ex) { err = "Could not reach the AMFI NAV file."; _logger.LogWarning(ex, "AMFI NAV download failed"); }
            foreach (var f in funds)
            {
                if (navs != null && navs.TryGetValue(f.Symbol!.Trim(), out var n)) { Apply(f, n.Nav, n.Date); updated++; }
                else { f.LastPriceError = err ?? $"Scheme code {f.Symbol} was not found in the AMFI file."; failed++; }
            }
        }

        // ── Stocks / ETFs (Yahoo chart) ──
        foreach (var s in rows.Where(r => r.Kind is InvestmentKind.Stock or InvestmentKind.Etf))
        {
            try
            {
                var (price, when) = await FetchYahooAsync(s.Symbol!.Trim(), ct);
                Apply(s, price, when); updated++;
            }
            catch (Exception ex)
            {
                s.LastPriceError = "Could not get a price for " + s.Symbol + ".";
                failed++;
                _logger.LogWarning(ex, "Price fetch failed for {Symbol}", s.Symbol);
            }
        }

        foreach (var r in rows.Where(r => r.Kind is not (InvestmentKind.MutualFund or InvestmentKind.Stock or InvestmentKind.Etf)))
        { r.LastPriceError = "Automatic value is only available for mutual funds, stocks and ETFs."; failed++; }

        await _context.SaveChangesAsync(ct);
        return new MarketRefreshResult(updated, failed, $"{updated} updated, {failed} could not be updated.");
    }

    private static void Apply(InvestmentDetails d, decimal price, DateTime priceDate)
    {
        d.LastPrice = price;
        d.LastPriceDate = priceDate;
        d.LastPriceError = null;
        d.CurrentValue = Math.Round(d.Units!.Value * price, 2);
        d.ValuedOn = priceDate;
    }

    private async Task<Dictionary<string, (decimal Nav, DateTime Date)>> DownloadAmfiAsync(HashSet<string> wanted, CancellationToken ct)
    {
        var client = _http.CreateClient("market");
        using var resp = await client.GetAsync(AmfiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        var result = new Dictionary<string, (decimal, DateTime)>();
        string? line;
        // Rows look like: Scheme Code;ISIN;ISIN;Scheme Name;Net Asset Value;Date   (header/category lines are skipped)
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            var p = line.Split(';');
            if (p.Length < 6) continue;
            var code = p[0].Trim();
            if (code.Length == 0 || !code.All(char.IsDigit) || !wanted.Contains(code)) continue;
            if (!decimal.TryParse(p[4].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var nav) || nav <= 0) continue;
            if (!DateTime.TryParseExact(p[5].Trim(), "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            result[code] = (nav, DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc));
        }
        return result;
    }

    private async Task<(decimal Price, DateTime When)> FetchYahooAsync(string symbol, CancellationToken ct)
    {
        var client = _http.CreateClient("market");
        var url = "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) + "?range=1d&interval=1d";
        using var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var meta = doc.RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");
        var price = meta.GetProperty("regularMarketPrice").GetDecimal();
        if (price <= 0) throw new InvalidOperationException("Non-positive price");
        var when = meta.TryGetProperty("regularMarketTime", out var t)
            ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime
            : DateTime.UtcNow;
        return (price, when);
    }
}
