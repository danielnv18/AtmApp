using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Application;

public sealed class AccountService(AtmDbContext db, TimeProvider clock) : IAccountService
{
    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Transaction>> GetHistoryAsync(Guid accountId, int limit, CancellationToken ct = default)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
        {
            throw new DomainException(DomainError.AccountNotFound);
        }

        return await db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.TimestampUtc)
            // Both sides of a transfer share a timestamp; the Id tie-break keeps paging stable.
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Transaction> DepositAsync(Guid accountId, Money amount, CancellationToken ct = default)
    {
        var account = await LoadAsync(accountId, ct);
        var ledgerRow = account.Credit(amount, clock.GetUtcNow());
        db.Transactions.Add(ledgerRow);
        await db.SaveChangesAsync(ct);
        return ledgerRow;
    }

    public async Task<Transaction> WithdrawAsync(Guid accountId, Money amount, CancellationToken ct = default)
    {
        var account = await LoadAsync(accountId, ct);
        var ledgerRow = account.Debit(amount, clock.GetUtcNow());
        db.Transactions.Add(ledgerRow);
        await db.SaveChangesAsync(ct);
        return ledgerRow;
    }

    public async Task<TransferReceipt> TransferAsync(Guid fromAccountId, Guid toAccountId, Money amount, CancellationToken ct = default)
    {
        if (fromAccountId == toAccountId)
        {
            throw new DomainException(DomainError.SameAccountTransfer);
        }

        var from = await LoadAsync(fromAccountId, ct);
        var to = await LoadAsync(toAccountId, ct);
        var correlationId = Guid.NewGuid();
        var now = clock.GetUtcNow();

        var withdrawal = from.Debit(amount, now, correlationId, $"Transfer to {to.Name}");
        var deposit = to.Credit(amount, now, correlationId, $"Transfer from {from.Name}");
        db.Transactions.AddRange(withdrawal, deposit);
        // One SaveChanges runs both balance updates and both ledger inserts in a single database
        // transaction. If either account changed underneath us, the whole transfer rolls back.
        await db.SaveChangesAsync(ct);

        return new TransferReceipt(correlationId, withdrawal, deposit);
    }

    private async Task<Account> LoadAsync(Guid accountId, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct)
        ?? throw new DomainException(DomainError.AccountNotFound);
}
