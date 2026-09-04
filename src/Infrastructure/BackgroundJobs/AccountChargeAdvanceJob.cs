using Application.Contracts;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.BackgroundJobs;

public class AccountChargeAdvanceJob
{
    private readonly IApplicationDbContext _context;

    public AccountChargeAdvanceJob(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AdvancePastDueChargesAsync()
    {
        var now = DateTime.UtcNow;

        var ccDetails = await _context.CreditCardDetails
            .Where(d => d.NextAnnualFeeDate.HasValue && d.NextAnnualFeeDate.Value < now)
            .ToListAsync();

        foreach (var d in ccDetails)
        {
            d.NextAnnualFeeDate = d.NextAnnualFeeDate!.Value.AddYears(1);
        }

        var bankDetails = await _context.BankAccountDetails
            .Where(d => d.NextPeriodicChargeDate.HasValue && d.NextPeriodicChargeDate.Value < now)
            .ToListAsync();

        foreach (var d in bankDetails)
        {
            d.NextPeriodicChargeDate = d.PeriodicChargeFrequency switch
            {
                InterestFrequency.Quarterly => d.NextPeriodicChargeDate!.Value.AddMonths(3),
                InterestFrequency.HalfYearly => d.NextPeriodicChargeDate!.Value.AddMonths(6),
                InterestFrequency.Yearly => d.NextPeriodicChargeDate!.Value.AddYears(1),
                _ => d.NextPeriodicChargeDate!.Value.AddYears(1) // Monthly/Daily don't
                     // meaningfully apply to a periodic account charge;
                     // fall back to yearly rather than silently loop
            };
        }

        if (ccDetails.Count > 0 || bankDetails.Count > 0)
        {
            await _context.SaveChangesAsync(default);
        }
    }
}
