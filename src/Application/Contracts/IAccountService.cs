using Application.Common.Models;
using Application.DTOs.Accounts;

namespace Application.Contracts;

public interface IAccountService
{
    Task<Result<PaginatedResult<AccountDto>>> GetPagedAccountsAsync(string userId, QueryParameters queryParams);
    Task<Result<List<AccountDto>>> GetAllAccountsAsync(string userId);
    Task<Result<AccountDto>> UpsertAccountAsync(string userId, UpsertAccountDto dto);
    Task<Result<bool>> DeleteAccountAsync(string userId, int accountId);
    Task<Result<bool>> MergeAccountsAsync(string userId, MergeAccountsDto dto);
    Task<Result<bool>> SetArchivedStatusAsync(string userId, int accountId, bool isArchived);
    Task<Result<LoanPaymentResultDto>> MakeLoanPaymentAsync(string userId, MakeLoanPaymentDto dto);
    Task<Result<AmortizationScheduleDto>> GetAmortizationScheduleAsync(string userId, int loanAccountId);
    Task<Result<CreditCardBreakdownDto>> GetCreditCardBreakdownAsync(string userId, int accountId);
    Task<Result<AccountDto>> AdjustBalanceAsync(string userId, AdjustBalanceDto dto);
    Task<Result<CreditCardBillResultDto>> RecordCreditCardBillAsync(string userId, RecordCreditCardBillDto dto);
    Task<Result<CreditCardPaymentBatchResultDto>> MakeCreditCardPaymentBatchAsync(string userId, MakeCreditCardPaymentBatchDto dto);
    Task<Result<CashbackInsightsDto>> GetCashbackInsightsAsync(string userId);
    Task<Result<List<AccountPaymentSuggestionDto>>> GetPaymentSuggestionsAsync(string userId, int creditCardAccountId, decimal amount);
    Task<Result<int>> BackfillOpeningBalancesAsync(string userId);
}