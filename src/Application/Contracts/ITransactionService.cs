using Application.Common.Models;
using Application.DTOs.Transactions;

namespace Application.Contracts;

public interface ITransactionService
{
    Task<Result<PaginatedResult<TransactionDto>>> GetTransactionsAsync(string userId, int accountId, QueryParameters queryParams);
    Task<Result<TransactionDto>> UpsertTransactionAsync(string userId, int accountId, UpsertTransactionDto dto);
    Task<Result<bool>> DeleteTransactionAsync(string userId, int accountId, int transactionId);
    /// <summary>Undo a plain delete (within a few minutes). Transfers, card/loan payments and cashback can't be undone.</summary>
    Task<Result<bool>> RestoreTransactionAsync(string userId, int accountId, int transactionId);
    /// <summary>Every tag the user has used, with how many live transactions carry it (for suggestions and filters).</summary>
    Task<Result<List<TagCountDto>>> GetTagsAsync(string userId);
    Task<Result<bool>> CreateTransferAsync(string userId, int sourceAccountId, CreateTransferDto dto);
    Task<Result<bool>> SwitchAccountAsync(string userId, int transactionId, int destinationAccountId);
    Task<Result<BulkInsertResponseDto>> BulkUpsertTransactionsAsync(string userId, BulkTransactionPayloadDto payload);
    Task<Result<TransactionPageResultDto>> GetAllTransactionsAsync(string userId, QueryParameters queryParams);
}