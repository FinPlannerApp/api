using System.Text.Json.Serialization;

namespace Application.DTOs.Transactions;

public class BulkTransactionPayloadDto
{
    [JsonPropertyName("transactions")]
    public required List<BulkTransactionEntryDto> Transactions { get; set; }

    /// <summary>Client-generated id for this submit. Re-sending the same id (double click, retry) is rejected instead of saving twice.</summary>
    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }
}
