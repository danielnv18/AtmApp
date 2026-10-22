using AtmApp.Api.Domain;

namespace AtmApp.Api.Application;

public interface IAccountService
{
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetHistoryAsync(Guid accountId, int limit, CancellationToken ct = default);
    Task<Transaction> DepositAsync(Guid accountId, Money amount, CancellationToken ct = default);
    Task<Transaction> WithdrawAsync(Guid accountId, Money amount, CancellationToken ct = default);
    Task<TransferReceipt> TransferAsync(Guid fromAccountId, Guid toAccountId, Money amount, CancellationToken ct = default);
}

public sealed record TransferReceipt(Guid CorrelationId, Transaction Withdrawal, Transaction Deposit);
