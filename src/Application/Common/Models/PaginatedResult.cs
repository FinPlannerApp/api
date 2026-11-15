namespace Application.Common.Models;

public class PaginatedResult<T>
{
    public List<T> Data { get; set; }
    public int TotalRecords { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }

    /// <summary>Keyset paging: true when there is a page after this one (no COUNT needed to know).</summary>
    public bool HasMore { get; set; }

    /// <summary>Keyset paging: send this back as the cursor to get the next page without an OFFSET.</summary>
    public string? NextCursor { get; set; }

    public PaginatedResult(List<T> data, int totalRecords, int pageNumber, int pageSize)
    {
        Data = data;
        TotalRecords = totalRecords;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}
