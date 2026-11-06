using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static System.FormattableString;

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

    public async Task<Transaction> DepositAsync(IdempotencyKey key, Guid accountId, Money amount, CancellationToken ct = default)
    {
        var rows = await OnceAsync(key, Invariant($"deposit {accountId} {amount.Value:0.00}"), async () =>
        {
            var account = await LoadAsync(accountId, ct);
            return [account.Deposit(amount, clock.GetUtcNow())];
        }, ct);
        return rows.Single();
    }

    public async Task<Transaction> WithdrawAsync(IdempotencyKey key, Guid accountId, Money amount, CancellationToken ct = default)
    {
        var rows = await OnceAsync(key, Invariant($"withdraw {accountId} {amount.Value:0.00}"), async () =>
        {
            var account = await LoadAsync(accountId, ct);
            return [account.Withdraw(amount, clock.GetUtcNow())];
        }, ct);
        return rows.Single();
    }

    public async Task<TransferReceipt> TransferAsync(
        IdempotencyKey key, Guid fromAccountId, Guid toAccountId, Money amount, CancellationToken ct = default)
    {
        if (fromAccountId == toAccountId)
        {
            throw new DomainException(DomainError.SameAccountTransfer);
        }

        var rows = await OnceAsync(key, Invariant($"transfer {fromAccountId} {toAccountId} {amount.Value:0.00}"), async () =>
        {
            var from = await LoadAsync(fromAccountId, ct);
            var to = await LoadAsync(toAccountId, ct);
            var correlationId = Guid.NewGuid();
            var now = clock.GetUtcNow();
            return
            [
                from.SendTransfer(amount, now, correlationId, $"Transfer to {to.Name}"),
                to.ReceiveTransfer(amount, now, correlationId, $"Transfer from {from.Name}"),
            ];
        }, ct);

        var transferOut = rows.Single(t => t.Type == TransactionType.TransferOut);
        var transferIn = rows.Single(t => t.Type == TransactionType.TransferIn);
        return new TransferReceipt(transferOut.CorrelationId!.Value, transferOut, transferIn);
    }

    // Runs an operation at most once per key. apply changes the tracked accounts and returns the
    // operation's new ledger rows; the key's record points at the first. A repeat of the same
    // request returns the rows the first run saved, and a different request with the key is rejected.
    private async Task<IReadOnlyList<Transaction>> OnceAsync(
        IdempotencyKey key, string request, Func<Task<Transaction[]>> apply, CancellationToken ct)
    {
        if (await SavedRowsAsync(key, request, ct) is { } saved)
        {
            return saved;
        }

        var rows = await apply();
        db.Transactions.AddRange(rows);
        db.IdempotencyRecords.Add(IdempotencyRecord.For(key, request, rows[0], clock.GetUtcNow()));
        try
        {
            // One SaveChanges runs the balance updates, the ledger inserts and the record insert in
            // a single database transaction. If an account changed underneath us, or the key was
            // saved meanwhile, all of it rolls back.
            await db.SaveChangesAsync(ct);
            return rows;
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key may have committed between our lookup and our
            // save. If so, answer as it did. Otherwise this was an ordinary conflict or failure.
            db.ChangeTracker.Clear();
            if (await SavedRowsAsync(key, request, ct) is { } winner)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<Transaction>?> SavedRowsAsync(IdempotencyKey key, string request, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key.Value, ct);
        if (record is null)
        {
            return null;
        }

        if (record.Request != request)
        {
            throw new IdempotencyKeyReusedException();
        }

        var first = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == record.TransactionId, ct);
        return first.CorrelationId is { } correlationId
            ? await db.Transactions.AsNoTracking().Where(t => t.CorrelationId == correlationId).ToListAsync(ct)
            : [first];
    }

    private async Task<Account> LoadAsync(Guid accountId, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct)
        ?? throw new AccountNotFoundException();
}
