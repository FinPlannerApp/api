namespace Application.Common.Helpers;

public static class UpiDeepLinkGenerator
{
    /// <summary>
    /// pa = payee address (the UPI VPA), pn = payee name, am = amount,
    /// cu = currency, tn = transaction note, tr = transaction reference.
    /// All free-text fields are URL-encoded — a payee name or note with
    /// spaces/special characters shouldn't produce a malformed link.
    /// </summary>
    public static string Generate(string upiId, string payeeName, decimal amount, string transactionNote, string transactionReference)
    {
        var encodedName = Uri.EscapeDataString(payeeName);
        var encodedNote = Uri.EscapeDataString(transactionNote);
        var encodedRef = Uri.EscapeDataString(transactionReference);

        return $"upi://pay?pa={upiId}&pn={encodedName}&am={amount:F2}&cu=INR&tn={encodedNote}&tr={encodedRef}";
    }
}
