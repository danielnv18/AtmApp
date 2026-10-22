namespace AtmApp.Api.Domain;

public sealed class Account
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public Guid ConcurrencyVersion { get; private set; }

    private Account() { }

    public static Account Open(string name, decimal openingBalance = 0m) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Balance = openingBalance,
        ConcurrencyVersion = Guid.NewGuid(),
    };

    public Transaction Credit(Money amount, DateTimeOffset timestampUtc, Guid? correlationId = null, string? description = null)
    {
        Balance += amount.Value;
        return Apply(amount, TransactionType.Credit, timestampUtc, correlationId, description);
    }

    public Transaction Debit(Money amount, DateTimeOffset timestampUtc, Guid? correlationId = null, string? description = null)
    {
        if (amount.Value > Balance)
        {
            throw new DomainException(DomainError.InsufficientFunds);
        }

        Balance -= amount.Value;
        return Apply(amount, TransactionType.Debit, timestampUtc, correlationId, description);
    }

    private Transaction Apply(Money amount, TransactionType type, DateTimeOffset timestampUtc, Guid? correlationId, string? description)
    {
        ConcurrencyVersion = Guid.NewGuid();
        return Transaction.Create(Id, amount, Balance, type, correlationId, timestampUtc, description);
    }
}
