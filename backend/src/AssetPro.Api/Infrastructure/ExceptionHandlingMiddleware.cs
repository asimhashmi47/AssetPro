using System.Diagnostics;
using AssetPro.Api.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Infrastructure;

/// <summary>
/// Converts every exception into the uniform <see cref="ApiError"/> body.
/// Expected failures keep their message; anything else is logged and returned as a generic 500.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to report.
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted) throw;
            await WriteErrorAsync(context, ex);
        }
    }

    private async Task WriteErrorAsync(HttpContext context, Exception ex)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        ApiError error;

        switch (ex)
        {
            case ValidationFailedException v:
                error = new ApiError(v.StatusCode, "Validation failed", v.Message, traceId, v.Errors);
                break;
            case AppException app:
                if (app.StatusCode >= 500) logger.LogError(ex, "Application error");
                else logger.LogInformation("Request rejected ({Status}): {Message}", app.StatusCode, app.Message);
                error = new ApiError(app.StatusCode, Title(app.StatusCode), app.Message, traceId);
                break;
            case DbUpdateConcurrencyException:
                logger.LogWarning(ex, "Concurrency conflict");
                error = new ApiError(409, "Conflict", "The record was changed by someone else. Refresh and try again.", traceId);
                break;
            case DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } }:
                // SQLITE_CONSTRAINT: a unique index or foreign key protected the data (e.g. a racing double assignment).
                logger.LogWarning(ex, "Database constraint violation");
                error = new ApiError(409, "Conflict", "This change conflicts with existing data (a duplicate or a record still in use).", traceId);
                break;
            case BadHttpRequestException bad:
                error = new ApiError(400, "Bad request", "The request could not be read.", traceId);
                logger.LogInformation(bad, "Bad request");
                break;
            default:
                logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                error = new ApiError(500, "Server error", "Something went wrong on our side. Please try again.", traceId);
                break;
        }

        context.Response.Clear();
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(error);
    }

    private static string Title(int status) => status switch
    {
        400 => "Bad request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflict",
        _ => "Error"
    };
}
