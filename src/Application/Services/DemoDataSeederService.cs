using Application.Contracts;
using Domain.Entities;
using Domain.Entities.Split;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services;

public class DemoDataSeederService
{
    private readonly IApplicationDbContext _context;

    public DemoDataSeederService(IApplicationDbContext context)
    {
        _context = context;
    }

    private static string ComputeDataHash(params object[] values)
    {
        using var sha256 = SHA256.Create();
        var combined = string.Join("|", values);
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes);
    }

    public async Task SeedDemoDataAsync(string userId)
    {
        // Check if demo data is already seeded for this user
        if (await _context.Accounts.AnyAsync(a => a.UserId == userId))
            return;

        // 1. Account Categories
        var catBank = new AccountCategory { Name = "Bank Account", UserId = userId, AccountType = AccountType.Bank };
        var catCc = new AccountCategory { Name = "Credit Card", UserId = userId, AccountType = AccountType.CreditCard, IsLiability = true };
        var catLoan = new AccountCategory { Name = "Loan Account", UserId = userId, AccountType = AccountType.Loan, IsLiability = true };
        var catCash = new AccountCategory { Name = "Cash & Wallet", UserId = userId, AccountType = AccountType.Cash };

        _context.AccountCategories.AddRange(catBank, catCc, catLoan, catCash);
        await _context.SaveChangesAsync();

        // 2. Transaction Categories
        var catSalary = new TransactionCategory { Name = "Salary & Income", UserId = userId };
        var catFreelance = new TransactionCategory { Name = "Freelance & Consulting", UserId = userId };
        var catGroceries = new TransactionCategory { Name = "Groceries & Supplies", UserId = userId };
        var catDining = new TransactionCategory { Name = "Dining & Restaurants", UserId = userId };
        var catUtilities = new TransactionCategory { Name = "Utilities & Bills", UserId = userId };
        var catShopping = new TransactionCategory { Name = "Shopping & Apparel", UserId = userId };
        var catTravel = new TransactionCategory { Name = "Travel & Fuel", UserId = userId };
        var catEntertainment = new TransactionCategory { Name = "Entertainment & Subscriptions", UserId = userId };

        _context.TransactionCategories.AddRange(catSalary, catFreelance, catGroceries, catDining, catUtilities, catShopping, catTravel, catEntertainment);
        await _context.SaveChangesAsync();

        // 3. Accounts
        var accHdfc = new Account
        {
            Name = "HDFC Salary Account",
            Balance = 185450.00m,
            UserId = userId,
            AccountCategoryId = catBank.Id,
            Purpose = "Monthly Salary & Main Expenses",
            BankAccountDetails = new BankAccountDetails
            {
                MinimumBalance = 10000.00m
            }
        };
        accHdfc.DataHash = ComputeDataHash(accHdfc.Name, accHdfc.Balance, userId);

        var accIcici = new Account
        {
            Name = "ICICI Wealth Savings",
            Balance = 420000.00m,
            UserId = userId,
            AccountCategoryId = catBank.Id,
            Purpose = "Emergency Fund & Long-term Savings",
            BankAccountDetails = new BankAccountDetails
            {
                MinimumBalance = 25000.00m
            }
        };
        accIcici.DataHash = ComputeDataHash(accIcici.Name, accIcici.Balance, userId);

        var accAxisCc = new Account
        {
            Name = "Axis Bank Select Credit Card",
            Balance = -42800.00m,
            UserId = userId,
            AccountCategoryId = catCc.Id,
            Purpose = "Dining & Reward Points Card",
            CreditCardDetails = new CreditCardDetails
            {
                CreditLimit = 200000.00m,
                MinimumDueAmount = 8500.00m,
                DueDate = DateTime.UtcNow.AddDays(10),
                StatementClosingDate = DateTime.UtcNow.AddDays(-10)
            }
        };
        accAxisCc.DataHash = ComputeDataHash(accAxisCc.Name, accAxisCc.Balance, userId);

        var accHomeLoan = new Account
        {
            Name = "HDFC Home Loan Account",
            Balance = -3500000.00m,
            UserId = userId,
            AccountCategoryId = catLoan.Id,
            Purpose = "Apartment Mortgage",
            LoanDetails = new LoanDetails
            {
                InterestRate = 8.5m,
                EmiAmount = 34500.00m,
                TenureMonths = 240,
                StartDate = DateTime.UtcNow.AddYears(-2)
            }
        };
        accHomeLoan.DataHash = ComputeDataHash(accHomeLoan.Name, accHomeLoan.Balance, userId);

        var accPaytm = new Account
        {
            Name = "Paytm Wallet & Cash",
            Balance = 3250.00m,
            UserId = userId,
            AccountCategoryId = catCash.Id,
            Purpose = "Daily Small Spends & Micro-transactions"
        };
        accPaytm.DataHash = ComputeDataHash(accPaytm.Name, accPaytm.Balance, userId);

        _context.Accounts.AddRange(accHdfc, accIcici, accAxisCc, accHomeLoan, accPaytm);
        await _context.SaveChangesAsync();

        // 4. Credit Card Bill
        var ccBill = new CreditCardBill
        {
            AccountId = accAxisCc.Id,
            StatementDate = DateTime.UtcNow.AddDays(-10),
            DueDate = DateTime.UtcNow.AddDays(10),
            BillAmount = 42800.00m,
            MinimumDue = 8500.00m,
            IsPaid = false,
            UserId = userId
        };
        _context.CreditCardBills.Add(ccBill);
        await _context.SaveChangesAsync();

        // 5. Merchants
        var mSwiggy = new Merchant { Name = "Swiggy Food Delivery", UserId = userId };
        var mAmazon = new Merchant { Name = "Amazon India", UserId = userId };
        var mUber = new Merchant { Name = "Uber India", UserId = userId };
        var mDmart = new Merchant { Name = "D-Mart Supermarket", UserId = userId };
        var mReliance = new Merchant { Name = "Reliance Digital", UserId = userId };

        _context.Merchants.AddRange(mSwiggy, mAmazon, mUber, mDmart, mReliance);
        await _context.SaveChangesAsync();

        // 6. Transactions
        var txList = new List<Transaction>
        {
            new Transaction
            {
                UserId = userId,
                AccountId = accHdfc.Id,
                Description = "Monthly Tech Salary",
                Amount = 145000.00m,
                Date = DateTime.UtcNow.AddDays(-2),
                Type = TransactionType.Income,
                TransactionCategoryId = catSalary.Id,
                Kind = TransactionKind.Normal
            },
            new Transaction
            {
                UserId = userId,
                AccountId = accHdfc.Id,
                Description = "Freelance UX Design Project",
                Amount = 35000.00m,
                Date = DateTime.UtcNow.AddDays(-8),
                Type = TransactionType.Income,
                TransactionCategoryId = catFreelance.Id,
                Kind = TransactionKind.Normal
            },
            new Transaction
            {
                UserId = userId,
                AccountId = accHdfc.Id,
                Description = "Monthly House Rent",
                Amount = 28000.00m,
                Date = DateTime.UtcNow.AddDays(-5),
                Type = TransactionType.Expense,
                TransactionCategoryId = catUtilities.Id,
                Kind = TransactionKind.Normal
            },
            new Transaction
            {
                UserId = userId,
                AccountId = accAxisCc.Id,
                Description = "Weekend Dining out at Toit",
                Amount = 3450.00m,
                Date = DateTime.UtcNow.AddDays(-3),
                Type = TransactionType.Expense,
                TransactionCategoryId = catDining.Id,
                MerchantId = mSwiggy.Id,
                Kind = TransactionKind.Normal
            },
            new Transaction
            {
                UserId = userId,
                AccountId = accAxisCc.Id,
                Description = "Monthly Grocery Haul",
                Amount = 11200.00m,
                Date = DateTime.UtcNow.AddDays(-6),
                Type = TransactionType.Expense,
                TransactionCategoryId = catGroceries.Id,
                MerchantId = mDmart.Id,
                Kind = TransactionKind.Normal
            },
            new Transaction
            {
                UserId = userId,
                AccountId = accAxisCc.Id,
                Description = "Amazon Great Summer Sale Purchase",
                Amount = 12400.00m,
                Date = DateTime.UtcNow.AddDays(-12),
                Type = TransactionType.Expense,
                TransactionCategoryId = catShopping.Id,
                MerchantId = mAmazon.Id,
                Kind = TransactionKind.Normal
            }
        };

        foreach (var t in txList)
        {
            t.DataHash = ComputeDataHash(t.Description, t.Amount, t.Date.ToString("o"), userId);
        }
        _context.Transactions.AddRange(txList);
        await _context.SaveChangesAsync();

        // 7. Budgets
        var bGroceries = new Budget
        {
            UserId = userId,
            TransactionCategoryId = catGroceries.Id,
            Amount = 15000.00m,
            Period = BudgetPeriod.Monthly,
            StartDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var bDining = new Budget
        {
            UserId = userId,
            TransactionCategoryId = catDining.Id,
            Amount = 8000.00m,
            Period = BudgetPeriod.Monthly,
            StartDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var bShopping = new Budget
        {
            UserId = userId,
            TransactionCategoryId = catShopping.Id,
            Amount = 10000.00m,
            Period = BudgetPeriod.Monthly,
            StartDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _context.Budgets.AddRange(bGroceries, bDining, bShopping);
        await _context.SaveChangesAsync();

        // 8. Recurring Transactions
        var rtNetflix = new RecurringTransaction
        {
            UserId = userId,
            AccountId = accAxisCc.Id,
            TransactionCategoryId = catEntertainment.Id,
            Description = "Netflix Premium 4K Plan",
            Amount = 649.00m,
            Frequency = RecurrenceFrequency.Monthly,
            NextProcessDate = DateTime.UtcNow.AddDays(4),
            IsActive = true
        };
        var rtElectricity = new RecurringTransaction
        {
            UserId = userId,
            AccountId = accHdfc.Id,
            TransactionCategoryId = catUtilities.Id,
            Description = "BESCOM Electricity Utility Bill",
            Amount = 2450.00m,
            Frequency = RecurrenceFrequency.Monthly,
            NextProcessDate = DateTime.UtcNow.AddDays(7),
            IsActive = true
        };
        _context.RecurringTransactions.AddRange(rtNetflix, rtElectricity);
        await _context.SaveChangesAsync();

        // 9. Split Group
        var splitGroup = new SplitGroup
        {
            Name = "Goa Beach Trip 2026",
            CreatedByUserId = userId,
            ShareToken = "goa-2026-demo"
        };
        _context.SplitGroups.Add(splitGroup);
        await _context.SaveChangesAsync();

        var m1 = new SplitGroupMember { SplitGroupId = splitGroup.Id, Name = "Subhrajit (You)", LinkedUserId = userId, UpiId = "subhrajit@okaxis" };
        var m2 = new SplitGroupMember { SplitGroupId = splitGroup.Id, Name = "Alice Sharma", UpiId = "alice@okicici" };
        var m3 = new SplitGroupMember { SplitGroupId = splitGroup.Id, Name = "Bob Verma", UpiId = "bob@okhdfc" };
        var m4 = new SplitGroupMember { SplitGroupId = splitGroup.Id, Name = "Charlie Patel", UpiId = "charlie@okaxis" };

        _context.SplitGroupMembers.AddRange(m1, m2, m3, m4);
        await _context.SaveChangesAsync();

        var splitExp1 = new SplitExpense
        {
            SplitGroupId = splitGroup.Id,
            Description = "Beach Villa Stay (3 Nights)",
            Amount = 18000.00m,
            Date = DateTime.UtcNow.AddDays(-4)
        };
        _context.SplitExpenses.Add(splitExp1);
        await _context.SaveChangesAsync();

        var payer1 = new SplitExpensePayer { SplitExpenseId = splitExp1.Id, SplitGroupMemberId = m1.Id, AmountPaid = 18000.00m };
        _context.SplitExpensePayers.Add(payer1);

        var p1 = new SplitExpenseParticipant { SplitExpenseId = splitExp1.Id, SplitGroupMemberId = m1.Id, ShareAmount = 4500.00m };
        var p2 = new SplitExpenseParticipant { SplitExpenseId = splitExp1.Id, SplitGroupMemberId = m2.Id, ShareAmount = 4500.00m };
        var p3 = new SplitExpenseParticipant { SplitExpenseId = splitExp1.Id, SplitGroupMemberId = m3.Id, ShareAmount = 4500.00m };
        var p4 = new SplitExpenseParticipant { SplitExpenseId = splitExp1.Id, SplitGroupMemberId = m4.Id, ShareAmount = 4500.00m };

        _context.SplitExpenseParticipants.AddRange(p1, p2, p3, p4);
        await _context.SaveChangesAsync();

        // 10. Goals
        var goalEmergency = new Goal
        {
            UserId = userId,
            Name = "6-Month Emergency Safety Net",
            TargetAmount = 500000.00m,
            ManualCurrentAmount = 350000.00m,
            TargetDate = DateTime.UtcNow.AddMonths(10)
        };
        var goalJapan = new Goal
        {
            UserId = userId,
            Name = "Tokyo & Kyoto Cherry Blossom Trip",
            TargetAmount = 250000.00m,
            ManualCurrentAmount = 95000.00m,
            TargetDate = DateTime.UtcNow.AddMonths(18)
        };
        _context.Goals.AddRange(goalEmergency, goalJapan);
        await _context.SaveChangesAsync();

        // 11. Decision Journal Entries
        var journal1 = new DecisionJournalEntry
        {
            UserId = userId,
            Title = "Purchasing iPhone 16 Pro Max",
            Reasoning = "Upgrading from 13 Pro. Camera improvements justify the cost for content creation.",
            Amount = 144900.00m,
            DecisionDate = DateTime.UtcNow.AddDays(-14)
        };
        var journal2 = new DecisionJournalEntry
        {
            UserId = userId,
            Title = "Ergonomic Mesh Home Office Chair",
            Reasoning = "Significant reduction in lower back pain during long coding sessions.",
            Amount = 18500.00m,
            DecisionDate = DateTime.UtcNow.AddDays(-30)
        };
        _context.DecisionJournalEntries.AddRange(journal1, journal2);
        await _context.SaveChangesAsync();

        // 12. 30-Day Money Challenge Enrollment & Progress
        var enrollment = new UserChallengeEnrollment
        {
            UserId = userId,
            StartedAt = DateTime.UtcNow.AddDays(-18)
        };
        _context.UserChallengeEnrollments.Add(enrollment);
        await _context.SaveChangesAsync();

        var challengeDays = await _context.ChallengeDays.OrderBy(d => d.DayNumber).ToListAsync();
        for (int i = 0; i < Math.Min(18, challengeDays.Count); i++)
        {
            _context.UserChallengeProgresses.Add(new UserChallengeProgress
            {
                UserId = userId,
                ChallengeDayId = challengeDays[i].Id,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow.AddDays(-18 + i),
                ReflectionText = $"Completed Day {i + 1} challenge successfully!"
            });
        }
        await _context.SaveChangesAsync();
    }
}
