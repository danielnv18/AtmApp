using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AtmApp.Api.Endpoints;

public static class AtmEndpoints
{
    private const int MinHistoryLimit = 1;
    private const int MaxHistoryLimit = 200;
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapAtmEndpoints(this IEndpointRouteBuilder routes)
    {
        var atm = routes.MapGroup("/api/atm");

        atm.MapGet("/accounts", async (IAccountService service, CancellationToken ct) =>
            (await service.GetAccountsAsync(ct)).Select(AccountResponse.From));

        atm.MapGet("/accounts/{id:guid}/history", async (
            Guid id, IAccountService service, CancellationToken ct, int limit = 50, string? cursor = null) =>
        {
            if (limit is < MinHistoryLimit or > MaxHistoryLimit)
            {
                return Results.Problem(
                    title: "Invalid limit",
                    detail: $"limit must be between {MinHistoryLimit} and {MaxHistoryLimit}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            HistoryCursor? after = null;
            if (cursor is not null && !CursorToken.TryDecode(cursor, out after))
            {
                return Results.Problem(
                    title: "Invalid cursor",
                    detail: "cursor is invalid. Use the nextCursor value from a previous response.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var page = await service.GetHistoryAsync(id, limit, after, ct);
            return Results.Ok(new HistoryResponse(
                page.Items.Select(TransactionResponse.From).ToList(),
                page.Next is null ? null : CursorToken.Encode(page.Next)));
        });

        atm.MapPost("/deposit", async Task<Results<Ok<TransactionResponse>, ProblemHttpResult>> (
            AmountRequest body, [FromHeader(Name = IdempotencyKeyHeader)] string? key, IAccountService service, CancellationToken ct) =>
        {
            if (!IdempotencyKey.TryParse(key, out var idempotencyKey))
            {
                return InvalidIdempotencyKey();
            }

            var amount = Money.Parse(body.Amount);
            return TypedResults.Ok(TransactionResponse.From(await service.DepositAsync(idempotencyKey, body.AccountId, amount, ct)));
        });

        atm.MapPost("/withdraw", async Task<Results<Ok<TransactionResponse>, ProblemHttpResult>> (
            AmountRequest body, [FromHeader(Name = IdempotencyKeyHeader)] string? key, IAccountService service, CancellationToken ct) =>
        {
            if (!IdempotencyKey.TryParse(key, out var idempotencyKey))
            {
                return InvalidIdempotencyKey();
            }

            var amount = Money.Parse(body.Amount);
            return TypedResults.Ok(TransactionResponse.From(await service.WithdrawAsync(idempotencyKey, body.AccountId, amount, ct)));
        });

        atm.MapPost("/transfer", async Task<Results<Ok<TransferResponse>, ProblemHttpResult>> (
            TransferRequest body, [FromHeader(Name = IdempotencyKeyHeader)] string? key, IAccountService service, CancellationToken ct) =>
        {
            if (!IdempotencyKey.TryParse(key, out var idempotencyKey))
            {
                return InvalidIdempotencyKey();
            }

            var amount = Money.Parse(body.Amount);
            var receipt = await service.TransferAsync(idempotencyKey, body.FromAccountId, body.ToAccountId, amount, ct);
            return TypedResults.Ok(new TransferResponse(
                receipt.CorrelationId,
                TransactionResponse.From(receipt.TransferOut),
                TransactionResponse.From(receipt.TransferIn)));
        });

        return routes;
    }

    private static ProblemHttpResult InvalidIdempotencyKey() => TypedResults.Problem(
        title: "Invalid idempotency key",
        detail: $"Send an {IdempotencyKeyHeader} header of 1 to {IdempotencyKey.MaxLength} characters, such as a new UUID for each operation.",
        statusCode: StatusCodes.Status400BadRequest);
}
