using AtmApp.Api.Domain;

namespace AtmApp.Api.Endpoints;

public sealed record AmountRequest(Guid AccountId, decimal Amount);

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);

public sealed record AccountResponse(Guid Id, string Name, decimal Balance)
{
    public static AccountResponse From(Account account) => new(account.Id, account.Name, account.Balance);
}

public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? CorrelationId,
    DateTimeOffset TimestampUtc,
    string? Description)
{
    public static TransactionResponse From(Transaction t) =>
        new(t.Id, t.AccountId, t.Type, t.Amount, t.BalanceAfter, t.CorrelationId, t.TimestampUtc, t.Description);
}

public sealed record HistoryResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor);

public sealed record TransferResponse(Guid CorrelationId, TransactionResponse TransferOut, TransactionResponse TransferIn);
