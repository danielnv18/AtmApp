using AtmApp.Api.Application;
using AtmApp.Api.Domain;

namespace AtmApp.Api.Endpoints;

public static class AtmEndpoints
{
    private const int MinHistoryLimit = 1;
    private const int MaxHistoryLimit = 200;

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

        atm.MapPost("/deposit", async (AmountRequest body, IAccountService service, CancellationToken ct) =>
        {
            var amount = Money.Parse(body.Amount);
            return TransactionResponse.From(await service.DepositAsync(body.AccountId, amount, ct));
        });

        atm.MapPost("/withdraw", async (AmountRequest body, IAccountService service, CancellationToken ct) =>
        {
            var amount = Money.Parse(body.Amount);
            return TransactionResponse.From(await service.WithdrawAsync(body.AccountId, amount, ct));
        });

        atm.MapPost("/transfer", async (TransferRequest body, IAccountService service, CancellationToken ct) =>
        {
            var amount = Money.Parse(body.Amount);
            var receipt = await service.TransferAsync(body.FromAccountId, body.ToAccountId, amount, ct);
            return new TransferResponse(
                receipt.CorrelationId,
                TransactionResponse.From(receipt.TransferOut),
                TransactionResponse.From(receipt.TransferIn));
        });

        return routes;
    }
}
