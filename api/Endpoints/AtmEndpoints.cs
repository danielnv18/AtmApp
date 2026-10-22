using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;

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
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = [$"limit must be between {MinHistoryLimit} and {MaxHistoryLimit}."],
                });
            }

            HistoryCursor? after = null;
            if (cursor is not null && !CursorToken.TryDecode(cursor, out after))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["cursor is invalid. Use the nextCursor value from a previous response."],
                });
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
                TransactionResponse.From(receipt.Withdrawal),
                TransactionResponse.From(receipt.Deposit));
        });

        return routes;
    }
}

public sealed record AmountRequest(Guid AccountId, decimal Amount);

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);

public sealed record AccountResponse(Guid Id, string Name, decimal Balance)
{
    public static AccountResponse From(Account account) => new(account.Id, account.Name, account.Balance);
}

public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? CorrelationId,
    DateTimeOffset TimestampUtc,
    string? Description)
{
    public static TransactionResponse From(Transaction t) =>
        new(t.Id, t.AccountId, t.Type.ToString(), t.Amount, t.BalanceAfter, t.CorrelationId, t.TimestampUtc, t.Description);
}

public sealed record HistoryResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor);

// Opaque to clients so the paging key can change without breaking them.
internal static class CursorToken
{
    public static string Encode(HistoryCursor cursor) =>
        Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes($"{cursor.TimestampUtc.UtcTicks}:{cursor.Id:N}"));

    public static bool TryDecode(string token, [NotNullWhen(true)] out HistoryCursor? cursor)
    {
        cursor = null;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(token)).Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                return false;
            }

            cursor = new HistoryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record TransferResponse(Guid CorrelationId, TransactionResponse Withdrawal, TransactionResponse Deposit);
