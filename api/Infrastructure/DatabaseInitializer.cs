using AtmApp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Infrastructure;

public static class DatabaseInitializer
{
    public const string OpeningDescription = "Opening deposit";

    public static async Task InitializeAsync(AtmDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        // EF creates new SQLite databases in WAL mode, which adds -wal and -shm files next to
        // atm.db. DELETE is SQLite's default mode and keeps the database as one file.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", ct);

        if (await db.Accounts.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var (name, opening) in new[] { ("Checking", 1000.00m), ("Savings", 500.00m) })
        {
            // Open at zero and Deposit the opening amount so the balance is backed by a ledger row.
            var account = Account.Open(name);
            var ledgerRow = account.Deposit(Money.Parse(opening), now, description: OpeningDescription);
            db.Accounts.Add(account);
            db.Transactions.Add(ledgerRow);
        }

        await db.SaveChangesAsync(ct);
    }
}
