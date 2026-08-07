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
}