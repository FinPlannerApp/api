using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

public class CreditCardBreakdownResult
{
    public decimal TotalOutstanding { get; set; }
    public decimal StatementOutstanding { get; set; }
    public decimal UnbilledOutstanding { get; set; }
    public DateTime? MostRecentStatementDate { get; set; }
}

public static class CreditCardStatementCalculator
{
    /// <summary>
    /// Computes the statement/unbilled split for ONE credit card account.
    /// Requires CreditCardDetails already loaded on the account (via
    /// .Include) — doesn't load it itself, since callers already have
    /// different query shapes around this.
    /// </summary>
    public static async Task<CreditCardBreakdownResult> CalculateAsync(IApplicationDbContext context, Account account)
    {
        var totalOutstanding = Math.Abs(account.Balance);
        var details = account.CreditCardDetails;

        if (details?.StatementClosingDate is null)
        {
            return new CreditCardBreakdownResult
            {
                TotalOutstanding = totalOutstanding,
                StatementOutstanding = 0,
                UnbilledOutstanding = totalOutstanding,
                MostRecentStatementDate = null
            };
        }

        var mostRecentStatementDate = RecurringDateHelper.GetMostRecentOccurrence(
            details.StatementClosingDate.Value, DateTime.UtcNow);

        var transactionsAfterStatement = await context.Transactions
            .Where(t => t.AccountId == account.Id && t.Date > mostRecentStatementDate)
            .ToListAsync();

        var netEffectAfterStatement = transactionsAfterStatement.Sum(t =>
            t.Type == TransactionType.Income ? t.Amount : -t.Amount);

        var balanceAsOfStatement = account.Balance - netEffectAfterStatement;
        var statementOutstanding = Math.Abs(balanceAsOfStatement);
        var unbilledOutstanding = Math.Max(0, totalOutstanding - statementOutstanding);

        return new CreditCardBreakdownResult
        {
            TotalOutstanding = totalOutstanding,
            StatementOutstanding = statementOutstanding,
            UnbilledOutstanding = unbilledOutstanding,
            MostRecentStatementDate = mostRecentStatementDate
        };
    }
}
