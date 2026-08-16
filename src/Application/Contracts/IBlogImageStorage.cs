namespace Application.Contracts;

public interface IBlogImageStorage
{
    /// <summary>
    /// Stores image bytes (already converted to WebP by the caller) and
    /// returns a URL the frontend can use directly in markdown. What
    /// that URL actually points to is entirely up to the implementation
    /// — a database-backed one returns an API endpoint, an R2-backed
    /// one returns a public bucket URL. The caller never needs to know
    /// the difference.
    /// </summary>
    Task<string> StoreAsync(byte[] webpData, string originalFileName);
}
