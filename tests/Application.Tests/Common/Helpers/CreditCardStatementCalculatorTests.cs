using System;
using System.Threading.Tasks;
using Application.Common.Helpers;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests.Common.Helpers;

/// <summary>
/// CreditCardStatementCalculator.CalculateAsync queries Transactions
/// directly, so it needs a real IApplicationDbContext — the EF Core
/// in-memory provider stands in for PostgreSQL here. Each test gets its
/// own uniquely-named in-memory database (via Guid) so tests never leak
/// state into one another.
/// </summary>
public class CreditCardStatementCalculatorTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new HttpContextAccessor());
    }

    [Fact]
    public async Task NoStatementClosingDateSet_TreatsEntireBalanceAsUnbilled()
    {
        await using var context = NewContext();

        var category = new AccountCategory { Name = "Credit Cards", UserId = "user-1", AccountType = AccountType.CreditCard };
        context.AccountCategories.Add(category);
        await context.SaveChangesAsync();

        var account = new Account { Name = "Test Card", Balance = -1000m, UserId = "user-1", AccountCategoryId = category.Id };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // No CreditCardDetails at all — details will be null.
        var result = await CreditCardStatementCalculator.CalculateAsync(context, account);

        result.TotalOutstanding.Should().Be(1000m);
        result.StatementOutstanding.Should().Be(0m);
        result.UnbilledOutstanding.Should().Be(1000m);
        result.MostRecentStatementDate.Should().BeNull();
    }

    [Fact]
    public async Task PurchaseAfterStatementDate_CountsAsUnbilled_NotStatementOutstanding()
    {
        await using var context = NewContext();

        var category = new AccountCategory { Name = "Credit Cards", UserId = "user-1", AccountType = AccountType.CreditCard };
        context.AccountCategories.Add(category);
        await context.SaveChangesAsync();

        var account = new Account { Name = "Test Card", Balance = -1000m, UserId = "user-1", AccountCategoryId = category.Id };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Statement closes on day 5 of each month.
        var details = new CreditCardDetails { AccountId = account.Id, StatementClosingDate = new DateTime(2026, 1, 5) };
        context.CreditCardDetails.Add(details);
        account.CreditCardDetails = details;
        await context.SaveChangesAsync();

        var mostRecentStatement = RecurringDateHelper.GetMostRecentOccurrence(details.StatementClosingDate.Value, DateTime.UtcNow);

        // A 200 purchase logged 5 days after the most recent statement
        // close — this portion of the balance should be unbilled.
        context.Transactions.Add(new Transaction
        {
            UserId = "user-1",
            Description = "Late purchase",
            Amount = 200m,
            Date = mostRecentStatement.AddDays(5),
            Type = TransactionType.Expense,
            AccountId = account.Id
        });
        await context.SaveChangesAsync();

        var result = await CreditCardStatementCalculator.CalculateAsync(context, account);

        result.TotalOutstanding.Should().Be(1000m);
        result.UnbilledOutstanding.Should().Be(200m);
        result.StatementOutstanding.Should().Be(800m);
    }

    [Fact]
    public async Task NoPurchasesAfterStatementDate_EntireBalanceIsStatementOutstanding()
    {
        await using var context = NewContext();

        var category = new AccountCategory { Name = "Credit Cards", UserId = "user-1", AccountType = AccountType.CreditCard };
        context.AccountCategories.Add(category);
        await context.SaveChangesAsync();

        var account = new Account { Name = "Test Card", Balance = -1000m, UserId = "user-1", AccountCategoryId = category.Id };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var details = new CreditCardDetails { AccountId = account.Id, StatementClosingDate = new DateTime(2026, 1, 5) };
        context.CreditCardDetails.Add(details);
        account.CreditCardDetails = details;
        await context.SaveChangesAsync();

        // No transactions logged at all after the statement date —
        // the whole balance should be attributed to the statement.
        var result = await CreditCardStatementCalculator.CalculateAsync(context, account);

        result.StatementOutstanding.Should().Be(1000m);
        result.UnbilledOutstanding.Should().Be(0m);
    }

    [Fact]
    public async Task StatementOutstandingAsOf_UsesTheStatementsOwnDate_NotTheLatestCycle()
    {
        await using var context = NewContext();

        var category = new AccountCategory { Name = "Credit Cards", UserId = "user-1", AccountType = AccountType.CreditCard };
        context.AccountCategories.Add(category);
        await context.SaveChangesAsync();

        // Owes 1000 today: 300 was spent after the old statement (still unpaid), 500 after the newer one.
        var account = new Account { Name = "Test Card", Balance = -1000m, UserId = "user-1", AccountCategoryId = category.Id };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var oldStatement = new DateTime(2026, 8, 5);
        var newStatement = new DateTime(2026, 9, 5);
        context.Transactions.AddRange(
            new Transaction { UserId = "user-1", Description = "Aug-Sep", Amount = 300m, Date = oldStatement.AddDays(10), Type = TransactionType.Expense, AccountId = account.Id },
            new Transaction { UserId = "user-1", Description = "After Sep", Amount = 500m, Date = newStatement.AddDays(3), Type = TransactionType.Expense, AccountId = account.Id });
        await context.SaveChangesAsync();

        // On the old statement date the card owed 1000 - 300 - 500 = 200; on the newer one 1000 - 500 = 500.
        (await CreditCardStatementCalculator.StatementOutstandingAsOfAsync(context, account, oldStatement)).Should().Be(200m);
        (await CreditCardStatementCalculator.StatementOutstandingAsOfAsync(context, account, newStatement)).Should().Be(500m);
    }
}
