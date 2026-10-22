namespace AtmApp.Api.Domain;

public sealed class Transaction
{
    public Guid Id { get; private init; }
    public Guid AccountId { get; private init; }
    public decimal Amount { get; private init; }
    public decimal BalanceAfter { get; private init; }
    public TransactionType Type { get; private init; }
    public Guid? CorrelationId { get; private init; }
    public DateTimeOffset TimestampUtc { get; private init; }
    public string? Description { get; private init; }

    private Transaction() { }

    internal static Transaction Create(
        Guid accountId,
        Money amount,
        decimal balanceAfter,
        TransactionType type,
        Guid? correlationId,
        DateTimeOffset timestampUtc,
        string? description) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        Amount = amount.Value,
        BalanceAfter = balanceAfter,
        Type = type,
        CorrelationId = correlationId,
        TimestampUtc = timestampUtc.ToUniversalTime(),
        Description = description,
    };
}
