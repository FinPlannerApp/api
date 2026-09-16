using System;
using System.Linq;
using System.Threading.Tasks;
using Application.Tests.Fixtures;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit;

public class AccountServiceTests
{
    [Fact]
    public async Task AddAccount_PersistsAccountToDatabase()
    {
        using var context = TestDbContextFactory.Create();

        var account = new Account
        {
            Name = "Savings Account",
            Balance = 15000.50m,
            UserId = "user-123",
            AccountCategoryId = 1,
            IsArchived = false
        };

        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var retrieved = context.Accounts.FirstOrDefault(a => a.UserId == "user-123");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Savings Account");
        retrieved.Balance.Should().Be(15000.50m);
    }

    [Fact]
    public async Task CalculateTotalBalance_SumsActiveAccountsOnly()
    {
        using var context = TestDbContextFactory.Create();

        context.Accounts.AddRange(
            new Account { Name = "Checking", Balance = 5000m, UserId = "user-1", AccountCategoryId = 1, IsArchived = false },
            new Account { Name = "Investment", Balance = 12000m, UserId = "user-1", AccountCategoryId = 1, IsArchived = false },
            new Account { Name = "Closed Old Account", Balance = 500m, UserId = "user-1", AccountCategoryId = 1, IsArchived = true }
        );
        await context.SaveChangesAsync();

        var activeTotal = context.Accounts
            .Where(a => a.UserId == "user-1" && !a.IsArchived && !a.IsDeleted)
            .Sum(a => a.Balance);

        activeTotal.Should().Be(17000m);
    }
}
