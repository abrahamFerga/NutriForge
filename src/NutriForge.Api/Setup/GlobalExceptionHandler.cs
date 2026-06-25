using System.ClientModel;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NutriForge.Application.Tracking;
using NutriForge.Infrastructure.Ai.Assistant;

namespace NutriForge.Api.Setup;

/// <summary>
/// Translates exceptions into RFC 9457 <c>application/problem+json</c> responses so every error
/// path is a Problem Details document (ARCH cross-cutting wiring).
/// </summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var (status, title, errors) = exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, "Validation failed", ve.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            FoodNotFoundException => (StatusCodes.Status404NotFound, "Food not found", null),
            AssistantNotConfiguredException => (StatusCodes.Status503ServiceUnavailable, "Assistant unavailable", null),
            // The AI provider returned an error (bad key, rate limit, outage). Degrade like the
            // unconfigured case rather than surfacing a raw 500 — every AI-backed endpoint benefits.
            ClientResultException => (StatusCodes.Status503ServiceUnavailable, "AI service temporarily unavailable", null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError ? null : exception.Message,
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception ex);
}
