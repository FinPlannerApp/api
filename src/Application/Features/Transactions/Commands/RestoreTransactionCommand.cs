using Application.Common.Models;
using Application.Contracts;
using MediatR;

namespace Application.Features.Transactions.Commands;

public record RestoreTransactionCommand(string UserId, int AccountId, int TransactionId) : IRequest<Result<bool>>;

public class RestoreTransactionCommandHandler : IRequestHandler<RestoreTransactionCommand, Result<bool>>
{
    private readonly ITransactionService _transactionService;
    public RestoreTransactionCommandHandler(ITransactionService transactionService) => _transactionService = transactionService;

    public Task<Result<bool>> Handle(RestoreTransactionCommand request, CancellationToken cancellationToken)
        => _transactionService.RestoreTransactionAsync(request.UserId, request.AccountId, request.TransactionId);
}
