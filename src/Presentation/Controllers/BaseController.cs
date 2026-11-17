using Application;
using Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace API.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("api")]
public abstract class BaseController : ControllerBase
{
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value is null ? NotFound() : Ok(ApiResult<object?>.Success(LimitList(result.Value)));
        }

        // Return 200 OK even for failures, wrapped in the Result object to mask the error status.
        // We MUST NOT return 'result' directly because the serializer will try to access 'Value', which throws on failure.
        return Ok(ApiResult<T>.Failure(result.Error)); 
    }

    /// <summary>The most items any list endpoint sends in one response, however large the data grows.</summary>
    private const int MaxListItems = 200;
    private const int MaxPageSize = 100;

    /// <summary>
    /// One rule for every list endpoint, so none can send an unbounded array, and one response model for pages:
    /// - with ?pageNumber=&amp;pageSize= the result is the global PaginatedResult (data, totalRecords, pageNumber, pageSize),
    ///   pageSize at most 100;
    /// - without them a plain list is sent as before, capped at 200 items, and marked X-Truncated when it was cut.
    /// X-Total-Count always says how many exist. Endpoints that already page in the database (the transaction, account
    /// and merchant lists) return PaginatedResult themselves and are not touched.
    /// </summary>
    private object? LimitList<T>(T value)
    {
        if (value is not System.Collections.IList list || value is string) return value;
        var total = list.Count;
        var query = Request.Query;
        var hasPage = query.TryGetValue("pageSize", out var ps) && int.TryParse(ps, out var requested) && requested > 0;
        Response.Headers["X-Total-Count"] = total.ToString();
        if (!hasPage && total <= MaxListItems) return value;

        var pageSize = hasPage ? Math.Min(int.Parse(ps.ToString()), MaxPageSize) : MaxListItems;
        var pageNumber = 1;
        if (hasPage && query.TryGetValue("pageNumber", out var pn) && int.TryParse(pn, out var number) && number > 0) pageNumber = number;

        var elementType = list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0] : typeof(object);
        var slice = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var item in list.Cast<object?>().Skip((pageNumber - 1) * pageSize).Take(pageSize)) slice.Add(item);

        if (hasPage)
            return Activator.CreateInstance(typeof(PaginatedResult<>).MakeGenericType(elementType), slice, total, pageNumber, pageSize);

        Response.Headers["X-Truncated"] = "true";
        return slice is T ? slice : value;
    }

    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok(result);
        }

        return Ok(result);
    }

}
