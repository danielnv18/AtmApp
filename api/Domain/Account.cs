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

    public Transaction Deposit(Money amount, DateTimeOffset timestampUtc, string? description = null) =>
        Credit(amount, TransactionType.Deposit, timestampUtc, null, description);

    public Transaction Withdraw(Money amount, DateTimeOffset timestampUtc, string? description = null) =>
        Debit(amount, TransactionType.Withdrawal, timestampUtc, null, description);

    public Transaction ReceiveTransfer(Money amount, DateTimeOffset timestampUtc, Guid correlationId, string? description = null) =>
        Credit(amount, TransactionType.TransferIn, timestampUtc, correlationId, description);

    public Transaction SendTransfer(Money amount, DateTimeOffset timestampUtc, Guid correlationId, string? description = null) =>
        Debit(amount, TransactionType.TransferOut, timestampUtc, correlationId, description);

    private Transaction Credit(Money amount, TransactionType type, DateTimeOffset timestampUtc, Guid? correlationId, string? description)
    {
        Balance += amount.Value;
        return Apply(amount, type, timestampUtc, correlationId, description);
    }

    private Transaction Debit(Money amount, TransactionType type, DateTimeOffset timestampUtc, Guid? correlationId, string? description)
    {
        if (amount.Value > Balance)
        {
            throw new DomainException(DomainError.InsufficientFunds);
        }

        Balance -= amount.Value;
        return Apply(amount, type, timestampUtc, correlationId, description);
    }

    private Transaction Apply(Money amount, TransactionType type, DateTimeOffset timestampUtc, Guid? correlationId, string? description)
    {
        // Advancing the sequence also marks this balance as changed, so a concurrent writer that
        // loaded the old sequence is rejected by the database (see AtmDbContext's concurrency token).
        LastSequence++;
        return Transaction.Create(Id, LastSequence, amount, Balance, type, correlationId, timestampUtc, description);
    }
}
