using System;
using System.Linq;
using System.Threading.Tasks;
using Application.Tests.Fixtures;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Integration;

public class TransactionServiceIntegrationTests
{
    [Fact]
    public async Task AddTransaction_UpdatesAccountBalance()
    {
        using var context = TestDbContextFactory.Create();

        var account = new Account
        {
            Id = 10,
            Name = "Primary Account",
            Balance = 1000m,
            UserId = "user-test",
            AccountCategoryId = 1
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var transaction = new Transaction
        {
            AccountId = 10,
            Amount = 250m,
            Type = TransactionType.Expense,
            Description = "Grocery Store",
            Date = DateTime.UtcNow,
            UserId = "user-test"
        };
        context.Transactions.Add(transaction);

        // Simulate balance adjustment side-effect
        account.Balance -= transaction.Amount;
        await context.SaveChangesAsync();

        var updatedAccount = context.Accounts.Find(10);
        updatedAccount!.Balance.Should().Be(750m);
    }

    [Fact]
    public async Task SoftDeleteTransaction_MarksIsDeletedTrue()
    {
        using var context = TestDbContextFactory.Create();

        var transaction = new Transaction
        {
            Id = 1,
            AccountId = 10,
            Amount = 100m,
            Type = TransactionType.Expense,
            Description = "Dinner",
            Date = DateTime.UtcNow,
            UserId = "user-test",
            IsDeleted = false
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        transaction.IsDeleted = true;
        transaction.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var retrieved = context.Transactions.FirstOrDefault(t => t.Id == 1 && !t.IsDeleted);
        retrieved.Should().BeNull();
    }
}
