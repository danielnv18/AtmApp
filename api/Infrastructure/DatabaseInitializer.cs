using AtmApp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Infrastructure;

public static class DatabaseInitializer
{
    public const string OpeningDescription = "Opening deposit";

    public static async Task InitializeAsync(AtmDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", ct);

        if (await db.Accounts.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var (name, opening) in new[] { ("Checking", 1000.00m), ("Savings", 500.00m) })
        {
            var account = Account.Open(name);
            var ledgerRow = account.Credit(Money.Parse(opening), now, description: OpeningDescription);
            db.Accounts.Add(account);
            db.Transactions.Add(ledgerRow);
        }

        await db.SaveChangesAsync(ct);
    }
}
