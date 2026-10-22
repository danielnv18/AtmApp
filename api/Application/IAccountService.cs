using AtmApp.Api.Domain;

namespace AtmApp.Api.Application;

public interface IAccountService
{
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default);
    Task<HistoryPage> GetHistoryAsync(Guid accountId, int limit, HistoryCursor? after = null, CancellationToken ct = default);
    Task<Transaction> DepositAsync(Guid accountId, Money amount, CancellationToken ct = default);
    Task<Transaction> WithdrawAsync(Guid accountId, Money amount, CancellationToken ct = default);
    Task<TransferReceipt> TransferAsync(Guid fromAccountId, Guid toAccountId, Money amount, CancellationToken ct = default);
}

// Position of the last row a client has seen, in history order (newest first, then Id).
public sealed record HistoryCursor(DateTimeOffset TimestampUtc, Guid Id)
{
    public static HistoryCursor After(Transaction row) => new(row.TimestampUtc, row.Id);
}

public sealed record HistoryPage(IReadOnlyList<Transaction> Items, HistoryCursor? Next);

public sealed record TransferReceipt(Guid CorrelationId, Transaction Withdrawal, Transaction Deposit);
