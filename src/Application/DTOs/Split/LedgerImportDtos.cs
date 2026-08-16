namespace Application.DTOs.Split;

public class ImportToLedgerDto
{
    public int GroupId { get; set; }
    public int AccountId { get; set; }
}

public class ImportToLedgerResultDto
{
    public int TransactionsCreated { get; set; }
    public int AlreadyImportedCount { get; set; }
}
