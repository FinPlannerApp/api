namespace Application.Contracts;

public record MarketRefreshResult(int Updated, int Failed, string Message);

/// <summary>Keeps investments with automatic valuation up to date (mutual funds from the AMFI NAV file, stocks/ETFs from a market price feed).</summary>
public interface IMarketPriceService
{
    /// <param name="userId">Only this user's holdings, or null for everyone (background job).</param>
    /// <param name="accountId">Only this holding, or null for all.</param>
    Task<MarketRefreshResult> RefreshAsync(string? userId, int? accountId, CancellationToken ct);
}
