using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Application;

public sealed class AccountService(AtmDbContext db, TimeProvider clock) : IAccountService
{
    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);

    public async Task<HistoryPage> GetHistoryAsync(
        Guid accountId, int limit, HistoryCursor? after = null, CancellationToken ct = default)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
        {
            throw new AccountNotFoundException();
        }

        var query = db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId);

        // Keyset paging: continue strictly after the cursor row in sort order. Unlike an offset,
        // rows inserted at the top between requests can't shift items into the next page.
        if (after is not null)
        {
            query = query.Where(t => t.Sequence < after.Sequence);
        }

        var rows = await query
            .OrderByDescending(t => t.Sequence)
            // One extra row tells us whether another page exists without a COUNT query.
            .Take(limit + 1)
            .ToListAsync(ct);

        var items = rows.Take(limit).ToList();
        var next = rows.Count > limit ? HistoryCursor.After(items[^1]) : null;
        return new HistoryPage(items, next);
    }

    public async Task<Transaction> DepositAsync(Guid accountId, Money amount, CancellationToken ct = default)
    {
        var account = await LoadAsync(accountId, ct);
        var ledgerRow = account.Deposit(amount, clock.GetUtcNow());
        db.Transactions.Add(ledgerRow);
        await db.SaveChangesAsync(ct);
        return ledgerRow;
    }

    public async Task<Transaction> WithdrawAsync(Guid accountId, Money amount, CancellationToken ct = default)
    {
        var account = await LoadAsync(accountId, ct);
        var ledgerRow = account.Withdraw(amount, clock.GetUtcNow());
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

        var withdrawal = from.SendTransfer(amount, now, correlationId, $"Transfer to {to.Name}");
        var deposit = to.ReceiveTransfer(amount, now, correlationId, $"Transfer from {from.Name}");
        db.Transactions.AddRange(withdrawal, deposit);
        // One SaveChanges runs both balance updates and both ledger inserts in a single database
        // transaction. If either account changed underneath us, the whole transfer rolls back.
        await db.SaveChangesAsync(ct);

        return new TransferReceipt(correlationId, withdrawal, deposit);
    }

    private async Task<Account> LoadAsync(Guid accountId, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct)
        ?? throw new AccountNotFoundException();
}
