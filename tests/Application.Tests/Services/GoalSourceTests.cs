using System;
using System.Linq;
using System.Threading.Tasks;
using Application.DTOs.Goals;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests.Services;

/// <summary>Where a goal's progress comes from: a bucket, else a linked account's live balance, else the manual amount.</summary>
public class GoalSourceTests
{
    private const string User = "user-1";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new HttpContextAccessor());

    private static async Task<Account> AddAccountAsync(ApplicationDbContext ctx, AccountType type, decimal balance, string name)
    {
        var category = new AccountCategory { Name = name + " category", UserId = User, AccountType = type };
        ctx.AccountCategories.Add(category);
        await ctx.SaveChangesAsync();
        var account = new Account { Name = name, Balance = balance, UserId = User, AccountCategoryId = category.Id };
        ctx.Accounts.Add(account);
        await ctx.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task GoalLinkedToAnAccount_FollowsItsLiveBalance()
    {
        await using var ctx = NewContext();
        var bank = await AddAccountAsync(ctx, AccountType.Bank, 50_000m, "Emergency account");
        var service = new GoalService(ctx);

        var created = await service.UpsertAsync(User, new UpsertGoalDto { Name = "Emergency fund", TargetAmount = 100_000m, LinkedAccountId = bank.Id });
        created.IsSuccess.Should().BeTrue();
        created.Value.Source.Should().Be("account");
        created.Value.CurrentAmount.Should().Be(50_000m);
        created.Value.LinkedAccountName.Should().Be("Emergency account");

        // A deposit moves the account balance: the goal follows with no edit.
        bank.Balance = 80_000m;
        await ctx.SaveChangesAsync();
        var goals = await service.GetAllAsync(User);
        goals.Value.Single().CurrentAmount.Should().Be(80_000m);
        goals.Value.Single().ProgressPercent.Should().Be(80m);
    }

    [Fact]
    public async Task ABucketWinsOverALinkedAccount()
    {
        await using var ctx = NewContext();
        var bank = await AddAccountAsync(ctx, AccountType.Bank, 90_000m, "Savings");
        var bucket = new SavingsBucket { UserId = User, AccountId = bank.Id, Name = "Trip", AllocatedAmount = 12_000m };
        ctx.SavingsBuckets.Add(bucket);
        await ctx.SaveChangesAsync();

        var result = await new GoalService(ctx).UpsertAsync(User, new UpsertGoalDto { Name = "Trip", TargetAmount = 40_000m, SavingsBucketId = bucket.Id, LinkedAccountId = bank.Id });

        result.Value.Source.Should().Be("bucket");
        result.Value.CurrentAmount.Should().Be(12_000m);
        result.Value.LinkedAccountId.Should().BeNull();
    }

    [Theory]
    [InlineData(AccountType.CreditCard)]
    [InlineData(AccountType.Loan)]
    public async Task ACardOrLoanCannotBeLinked(AccountType type)
    {
        await using var ctx = NewContext();
        var liability = await AddAccountAsync(ctx, type, -5_000m, "Debt");

        var result = await new GoalService(ctx).UpsertAsync(User, new UpsertGoalDto { Name = "Nope", TargetAmount = 1_000m, LinkedAccountId = liability.Id });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Goal.AccountNotSavings");
    }

    [Fact]
    public async Task AnotherUsersAccountCannotBeLinked()
    {
        await using var ctx = NewContext();
        var category = new AccountCategory { Name = "Banks", UserId = "someone-else", AccountType = AccountType.Bank };
        ctx.AccountCategories.Add(category);
        await ctx.SaveChangesAsync();
        var foreign = new Account { Name = "Theirs", Balance = 1m, UserId = "someone-else", AccountCategoryId = category.Id };
        ctx.Accounts.Add(foreign);
        await ctx.SaveChangesAsync();

        var result = await new GoalService(ctx).UpsertAsync(User, new UpsertGoalDto { Name = "Mine", TargetAmount = 10m, LinkedAccountId = foreign.Id });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Goal.AccountNotFound");
    }
}
