namespace Application.DTOs.Dashboard;

public class PublicStatsDto
{
    public int TotalUsers { get; set; }
    public int TotalAccounts { get; set; }
    public int TotalTransactions { get; set; }
    public decimal TotalTransactionVolume { get; set; }
}
