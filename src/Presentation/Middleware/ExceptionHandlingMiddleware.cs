using Application;
using Application.Common.Models;
using System.Net;
using System.Text.Json;

namespace API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception has occurred.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        // User Request: Return 200 OK even for unhandled exceptions to mask 500 errors.
        context.Response.StatusCode = (int)HttpStatusCode.OK;

        // The full exception (including the inner exception — the actual
        // SQL error, in a DbUpdateException's case) is already logged
        // above in InvokeAsync via _logger.LogError. What goes back to
        // the client is deliberately generic instead — the raw message
        // can expose internal details (table/column names, connection
        // fragments) and is rarely something a user could act on anyway.
        // The real diagnostic value lives in your logs, not the response.
        var error = new Error("Server.Exception", "Something went wrong on our end. Please try again.");
        var response = ApiResult<object>.Failure(error);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

        await context.Response.WriteAsync(jsonResponse);
    }
}
