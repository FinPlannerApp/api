using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Domain.Enums;

namespace Application.DTOs.Transactions;

public class UpsertTransactionDto
{
    public int? Id { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int? TransactionCategoryId { get; set; }
    public int? MerchantId { get; set; }
}