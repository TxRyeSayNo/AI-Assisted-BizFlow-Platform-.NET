using BizFlow.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace BizFlow.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ApplicationFault fault)
        {
            context.Response.StatusCode = ApiErrors.Status(fault.Kind);
            await context.Response.WriteAsJsonAsync(ApiErrors.Create(context, fault.Code, fault.Message, fault.Details), cancellationToken);
            return true;
        }

        if (exception is DbUpdateConcurrencyException)
        {
            logger.LogWarning(exception, "Optimistic concurrency conflict; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(ApiErrors.Create(context, "CONCURRENCY.CONFLICT",
                "The record was modified by another operation. Please refresh and try again."), cancellationToken);
            return true;
        }

        // Avoid sending or logging exception text containing SQL, submitted credentials or provider payloads.
        logger.LogError(exception, "Unhandled error {ExceptionType}; trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(ApiErrors.Create(context, "INTERNAL.ERROR",
            "An unexpected error occurred."), cancellationToken);
        return true;
    }
}
