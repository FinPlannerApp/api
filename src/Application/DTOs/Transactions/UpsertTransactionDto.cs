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
    /// <summary>Optional note, up to 500 characters.</summary>
    public string? Notes { get; set; }
    /// <summary>Optional tags (max 8, 30 characters each); normalised to lower case on save.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Server-set by the recurring job only; ignored from client JSON.</summary>
    [JsonIgnore]
    public int? RecurringTransactionId { get; set; }
    [JsonIgnore]
    public DateTime? RecurringOccurrenceDate { get; set; }

    /// <summary>Server-set by statement import only; ignored from client JSON.</summary>
    [JsonIgnore]
    public string? ImportRef { get; set; }
    [JsonIgnore]
    public DateTime? ReconciledAt { get; set; }
}
