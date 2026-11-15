using System.Text.Json.Serialization;
using Domain.Enums;

namespace Application.DTOs.Transactions;

/// <summary>One normalised row from a bank/card statement. Parsed and redacted in the browser;
/// the statement file itself is never uploaded.</summary>
public class StatementRowDto
{
    [JsonPropertyName("date")] public DateTime Date { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    /// <summary>Always positive.</summary>
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("type")] public TransactionType Type { get; set; }
    /// <summary>Optional: hash (hex, max 64) of the bank's reference number.</summary>
    [JsonPropertyName("ref")] public string? Ref { get; set; }
}

public class StatementImportPreviewRequestDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    [JsonPropertyName("rows")] public List<StatementRowDto> Rows { get; set; } = new();
}

public class StatementRowMatchDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    /// <summary>New | Duplicate | Possible | Imported</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "New";
    [JsonPropertyName("matchedTransactionId")] public int? MatchedTransactionId { get; set; }
    [JsonPropertyName("matchedDescription")] public string? MatchedDescription { get; set; }
    [JsonPropertyName("matchedDate")] public DateTime? MatchedDate { get; set; }
}

public class StatementImportPreviewDto
{
    [JsonPropertyName("matches")] public List<StatementRowMatchDto> Matches { get; set; } = new();
    [JsonPropertyName("newCount")] public int NewCount => Matches.Count(m => m.Status == "New");
    [JsonPropertyName("duplicateCount")] public int DuplicateCount => Matches.Count(m => m.Status == "Duplicate");
    [JsonPropertyName("possibleCount")] public int PossibleCount => Matches.Count(m => m.Status == "Possible");
    [JsonPropertyName("importedCount")] public int ImportedCount => Matches.Count(m => m.Status == "Imported");
}

public class StatementCommitRowDto : StatementRowDto
{
    /// <summary>Add | Merge | Skip</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = "Skip";
    [JsonPropertyName("mergeTransactionId")] public int? MergeTransactionId { get; set; }
    [JsonPropertyName("categoryId")] public int? CategoryId { get; set; }
}

public class StatementImportCommitDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    [JsonPropertyName("batchId")] public string? BatchId { get; set; }
    [JsonPropertyName("rows")] public List<StatementCommitRowDto> Rows { get; set; } = new();
}

public class StatementImportResultDto
{
    [JsonPropertyName("added")] public int Added { get; set; }
    [JsonPropertyName("merged")] public int Merged { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
    [JsonPropertyName("failures")] public List<BulkInsertFailureDto> Failures { get; set; } = new();
}
