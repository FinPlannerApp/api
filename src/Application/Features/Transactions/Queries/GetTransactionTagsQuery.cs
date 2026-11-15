using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using MediatR;

namespace Application.Features.Transactions.Queries;

public record GetTransactionTagsQuery(string UserId) : IRequest<Result<List<TagCountDto>>>;

public class GetTransactionTagsQueryHandler : IRequestHandler<GetTransactionTagsQuery, Result<List<TagCountDto>>>
{
    private readonly ITransactionService _transactionService;
    public GetTransactionTagsQueryHandler(ITransactionService transactionService) => _transactionService = transactionService;

    public Task<Result<List<TagCountDto>>> Handle(GetTransactionTagsQuery request, CancellationToken ct)
        => _transactionService.GetTagsAsync(request.UserId);
}
