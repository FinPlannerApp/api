using Microsoft.AspNetCore.Mvc;

namespace Application;

public class QueryParameters
{
    private const int MaxPageSize = 1000;
    private int _pageSize = 10;

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
    }

    public string? SortBy { get; set; } = null;

    public string SortOrder { get; set; } = "asc";

    public string? GlobalSearch { get; set; }

    public Dictionary<string, string> Filters { get; set; } = new Dictionary<string, string>();

    [FromQuery(Name = "month")]
    public int? Month { get; set; }

    [FromQuery(Name = "year")]
    public int? Year { get; set; }

    [FromQuery(Name = "accountCategoryId")]
    public int? AccountCategoryId { get; set; }

    [FromQuery(Name = "transactionCategoryId")]
    public int? TransactionCategoryId { get; set; }
}
