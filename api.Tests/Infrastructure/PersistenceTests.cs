using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Api.Tests.Infrastructure;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atm-test-{Guid.NewGuid():N}.db");

    private AtmDbContext NewContext() => new(new DbContextOptionsBuilder<AtmDbContext>()
        .UseSqlite($"Data Source={_path};Default Timeout=5")
        .Options);

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db, TimeProvider.System);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Seeding_creates_opening_ledger_rows_and_is_not_repeated_on_restart()
    {
        await using (var db = NewContext())
        {
            await DatabaseInitializer.InitializeAsync(db, TimeProvider.System);
        }

        await using var verify = NewContext();
        var accounts = await verify.Accounts.OrderBy(a => a.Name).ToListAsync();
        var ledger = await verify.Transactions.ToListAsync();

        Assert.Equal(["Checking", "Savings"], accounts.Select(a => a.Name));
        Assert.Equal([1000.00m, 500.00m], accounts.Select(a => a.Balance));
        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, t =>
        {
            Assert.Equal(TransactionType.Deposit, t.Type);
            Assert.Equal(DatabaseInitializer.OpeningDescription, t.Description);
            Assert.Equal(accounts.Single(a => a.Id == t.AccountId).Balance, t.BalanceAfter);
        });
    }

    [Theory]
    [InlineData("UPDATE \"Accounts\" SET \"Balance\" = -1")]
    [InlineData("UPDATE \"Transactions\" SET \"Amount\" = 0")]
    [InlineData("UPDATE \"Transactions\" SET \"BalanceAfter\" = -1")]
    public async Task Engine_rejects_writes_that_bypass_the_domain(string sql)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Contains("CHECK constraint failed", ex.Message);
    }

    [Fact]
    public async Task Stale_account_write_is_rejected_instead_of_overwriting()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Accounts.FirstAsync(x => x.Name == "Checking");
        var b = await second.Accounts.FirstAsync(x => x.Name == "Checking");

        first.Transactions.Add(a.Debit(Money.Parse(100m), DateTimeOffset.UtcNow));
        await first.SaveChangesAsync();

        second.Transactions.Add(b.Debit(Money.Parse(100m), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = NewContext();
        Assert.Equal(900m, (await verify.Accounts.SingleAsync(x => x.Name == "Checking")).Balance);
    }

    [Fact]
    public async Task Newest_transaction_sorts_first_in_sql_and_cents_round_trip_exactly()
    {
        await using var db = NewContext();
        var account = await db.Accounts.FirstAsync(x => x.Name == "Savings");
        var later = DateTimeOffset.UtcNow.AddMinutes(1);
        db.Transactions.Add(account.Credit(Money.Parse(0.33m), later));
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        var newest = await verify.Transactions
            .Where(t => t.AccountId == account.Id)
            .OrderByDescending(t => t.TimestampUtc)
            .FirstAsync();

        Assert.Equal(0.33m, newest.Amount);
        Assert.Equal(500.33m, newest.BalanceAfter);
        Assert.Equal(later.UtcTicks, newest.TimestampUtc.UtcTicks);
    }
}

