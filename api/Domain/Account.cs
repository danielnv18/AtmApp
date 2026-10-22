namespace AtmApp.Api.Domain;

public sealed class Account
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public long LastSequence { get; private set; }

    // For EF Core materialization only; code creates accounts through Open.
    private Account() { }

    // Accounts open empty: every cent of a balance must be backed by a ledger row.
    public static Account Open(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
    };

    public Transaction Credit(Money amount, DateTimeOffset timestampUtc, Guid? correlationId = null, string? description = null)
    {
        Balance += amount.Value;
        return Apply(amount, TransactionType.Deposit, timestampUtc, correlationId, description);
    }

    public Transaction Debit(Money amount, DateTimeOffset timestampUtc, Guid? correlationId = null, string? description = null)
    {
        if (amount.Value > Balance)
        {
            throw new DomainException(DomainError.InsufficientFunds);
        }

        Balance -= amount.Value;
        return Apply(amount, TransactionType.Withdrawal, timestampUtc, correlationId, description);
    }

    private Transaction Apply(Money amount, TransactionType type, DateTimeOffset timestampUtc, Guid? correlationId, string? description)
    {
        // Advancing the sequence also marks this balance as changed, so a concurrent writer that
        // loaded the old sequence is rejected by the database (see AtmDbContext's concurrency token).
        LastSequence++;
        return Transaction.Create(Id, LastSequence, amount, Balance, type, correlationId, timestampUtc, description);
    }
}
