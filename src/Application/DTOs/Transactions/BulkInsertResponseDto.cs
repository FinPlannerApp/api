using System.Text.Json.Serialization;

namespace Application.DTOs.Transactions;

public class BulkInsertResponseDto
{
    [JsonPropertyName("successfulCount")]
    public int SuccessfulCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    /// <summary>True when failures caused the whole batch to be rolled back: nothing was saved.</summary>
    [JsonPropertyName("rolledBack")]
    public bool RolledBack { get; set; }

    [JsonPropertyName("failedTransactions")]
    public List<BulkInsertFailureDto> FailedTransactions { get; set; } = new();
}

public class BulkInsertFailureDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public class BulkRowIssueDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }
    /// <summary>error = would fail on save; warning = saveable but suspicious.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "warning";
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class BulkValidationResultDto
{
    [JsonPropertyName("issues")]
    public List<BulkRowIssueDto> Issues { get; set; } = new();
    [JsonPropertyName("errorCount")]
    public int ErrorCount => Issues.Count(i => i.Severity == "error");
    [JsonPropertyName("warningCount")]
    public int WarningCount => Issues.Count(i => i.Severity == "warning");
}
