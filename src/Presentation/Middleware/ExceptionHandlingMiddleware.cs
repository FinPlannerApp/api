using Application;
using Application.Common.Models;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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

        Error error;
        if (exception is ValidationException valEx)
        {
            var firstError = valEx.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed.";
            error = new Error("Validation.Error", firstError);
        }
        else if (exception is DbUpdateException dbEx)
        {
            var msg = dbEx.InnerException?.Message ?? dbEx.Message;
            if (msg.Contains("23505") || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                error = new Error("Database.Duplicate", "A record with this information already exists.");
            }
            else if (msg.Contains("23503") || msg.Contains("foreign key", StringComparison.OrdinalIgnoreCase))
            {
                error = new Error("Database.ForeignKey", "Referenced record was not found or is invalid.");
            }
            else
            {
                error = new Error("Database.Error", "A database error occurred while saving changes.");
            }
        }
        else
        {
            error = new Error("Server.Exception", "Something went wrong on our end. Please try again.");
        }

        var response = ApiResult<object>.Failure(error);

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

        await context.Response.WriteAsync(jsonResponse);
    }
}
