using System.Globalization;
using AtmApp.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Endpoints;

public sealed class AtmExceptionHandler(IProblemDetailsService problemDetails, ILogger<AtmExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            DomainException { Error: DomainError.InvalidAmount } =>
                (StatusCodes.Status400BadRequest, "Invalid amount",
                    $"Amount must be greater than 0, at most {Money.MaxValue.ToString("N2", CultureInfo.InvariantCulture)}, and have at most 2 decimal places."),
            DomainException { Error: DomainError.SameAccountTransfer } =>
                (StatusCodes.Status400BadRequest, "Same account transfer", "Source and destination accounts must differ."),
            DomainException { Error: DomainError.AccountNotFound } =>
                (StatusCodes.Status404NotFound, "Account not found", "No account exists with the given id."),
            DomainException { Error: DomainError.InsufficientFunds } =>
                (StatusCodes.Status422UnprocessableEntity, "Insufficient funds", "The account balance is too low for this operation."),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Concurrent update",
                    "The account was changed by another request. Reload and try again."),
            // Malformed JSON, a missing body or field, or an unparsable query value: keep the
            // framework's 400 instead of reporting a server error.
            BadHttpRequestException bad =>
                (bad.StatusCode, "Bad request", "The request is malformed or is missing a required value."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
