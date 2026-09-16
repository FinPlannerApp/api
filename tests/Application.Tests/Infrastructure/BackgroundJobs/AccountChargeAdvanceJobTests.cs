using System;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests.Infrastructure.BackgroundJobs;

/// <summary>
/// AccountChargeAdvanceJob queries and updates CreditCardDetails and
/// BankAccountDetails directly, so it needs a real IApplicationDbContext
/// — the EF Core in-memory provider stands in for PostgreSQL. Verified
/// directly against the real implementation in api-main/src/
/// Infrastructure/BackgroundJobs/AccountChargeAdvanceJob.cs.
/// </summary>
public class AccountChargeAdvanceJobTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new HttpContextAccessor());
    }

    private static async Task<Account> SeedAccountAsync(ApplicationDbContext context, AccountType type)
    {
        var category = new AccountCategory { Name = type.ToString(), UserId = "user-1", AccountType = type };
        context.AccountCategories.Add(category);
        await context.SaveChangesAsync();

        var account = new Account { Name = "Test Account", Balance = 0m, UserId = "user-1", AccountCategoryId = category.Id };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task PastDueAnnualFee_AdvancesByExactlyOneYear()
    {
        await using var context = NewContext();
        var account = await SeedAccountAsync(context, AccountType.CreditCard);

        var pastDueDate = DateTime.UtcNow.AddDays(-5);
        var details = new CreditCardDetails { AccountId = account.Id, NextAnnualFeeDate = pastDueDate };
        context.CreditCardDetails.Add(details);
        await context.SaveChangesAsync();

        var job = new AccountChargeAdvanceJob(context);
        await job.AdvancePastDueChargesAsync();

        var updated = await context.CreditCardDetails.FindAsync(details.Id);
        updated!.NextAnnualFeeDate.Should().Be(pastDueDate.AddYears(1));
    }

    [Fact]
    public async Task AnnualFeeNotYetDue_IsNotAdvanced()
    {
        await using var context = NewContext();
        var account = await SeedAccountAsync(context, AccountType.CreditCard);

        var futureDate = DateTime.UtcNow.AddDays(30);
        var details = new CreditCardDetails { AccountId = account.Id, NextAnnualFeeDate = futureDate };
        context.CreditCardDetails.Add(details);
        await context.SaveChangesAsync();

        var job = new AccountChargeAdvanceJob(context);
        await job.AdvancePastDueChargesAsync();

        var updated = await context.CreditCardDetails.FindAsync(details.Id);
        updated!.NextAnnualFeeDate.Should().Be(futureDate); // unchanged
    }

    [Fact]
    public async Task PastDueQuarterlyBankCharge_AdvancesByExactlyThreeMonths()
    {
        await using var context = NewContext();
        var account = await SeedAccountAsync(context, AccountType.Bank);

        var pastDueDate = DateTime.UtcNow.AddDays(-2);
        var details = new BankAccountDetails
        {
            AccountId = account.Id,
            NextPeriodicChargeDate = pastDueDate,
            PeriodicChargeFrequency = InterestFrequency.Quarterly
        };
        context.BankAccountDetails.Add(details);
        await context.SaveChangesAsync();

        var job = new AccountChargeAdvanceJob(context);
        await job.AdvancePastDueChargesAsync();

        var updated = await context.BankAccountDetails.FindAsync(details.Id);
        updated!.NextPeriodicChargeDate.Should().Be(pastDueDate.AddMonths(3));
    }

    [Fact]
    public async Task PastDueHalfYearlyBankCharge_AdvancesByExactlySixMonths()
    {
        await using var context = NewContext();
        var account = await SeedAccountAsync(context, AccountType.Bank);

        var pastDueDate = DateTime.UtcNow.AddDays(-2);
        var details = new BankAccountDetails
        {
            AccountId = account.Id,
            NextPeriodicChargeDate = pastDueDate,
            PeriodicChargeFrequency = InterestFrequency.HalfYearly
        };
        context.BankAccountDetails.Add(details);
        await context.SaveChangesAsync();

        var job = new AccountChargeAdvanceJob(context);
        await job.AdvancePastDueChargesAsync();

        var updated = await context.BankAccountDetails.FindAsync(details.Id);
        updated!.NextPeriodicChargeDate.Should().Be(pastDueDate.AddMonths(6));
    }
}
