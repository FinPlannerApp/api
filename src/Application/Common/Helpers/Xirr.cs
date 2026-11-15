namespace Application.Common.Helpers;

/// <summary>Annualised return for dated cash flows (negative = money you put in, positive = money you got out / current value).</summary>
public static class Xirr
{
    public static double? Compute(IReadOnlyList<(DateTime Date, decimal Amount)> flows)
    {
        if (flows.Count < 2) return null;
        if (!flows.Any(f => f.Amount < 0) || !flows.Any(f => f.Amount > 0)) return null;
        var t0 = flows.Min(f => f.Date);
        var span = (flows.Max(f => f.Date) - t0).TotalDays;
        if (span < 30) return null; // an annualised figure over a few days is meaningless

        double Npv(double r) => flows.Sum(f => (double)f.Amount / Math.Pow(1 + r, (f.Date - t0).TotalDays / 365.0));

        double lo = -0.99, hi = 10.0;
        double fLo = Npv(lo), fHi = Npv(hi);
        if (double.IsNaN(fLo) || double.IsNaN(fHi) || fLo * fHi > 0) return null;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var fMid = Npv(mid);
            if (Math.Abs(fMid) < 0.005) return mid;
            if (fLo * fMid <= 0) { hi = mid; } else { lo = mid; fLo = fMid; }
        }
        return (lo + hi) / 2;
    }
}
