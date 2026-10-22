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
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(_path + suffix);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task First_start_seeds_two_accounts_each_with_an_opening_deposit()
    {
        await using var db = NewContext();
        var accounts = await db.Accounts.OrderBy(a => a.Name).ToListAsync();
        var ledger = await db.Transactions.ToListAsync();

        Assert.Equal(["Checking", "Savings"], accounts.Select(a => a.Name));
        Assert.Equal([1000.00m, 500.00m], accounts.Select(a => a.Balance));
        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, t =>
        {
            Assert.Equal(TransactionType.Deposit, t.Type);
            Assert.Equal("Opening deposit", t.Description);
            Assert.Equal(accounts.Single(a => a.Id == t.AccountId).Balance, t.Amount);
        });
    }

    [Fact]
    public async Task Restarting_the_app_does_not_seed_again()
    {
        await using (var db = NewContext())
        {
            await DatabaseInitializer.InitializeAsync(db, TimeProvider.System);
        }

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Accounts.CountAsync());
        Assert.Equal(2, await verify.Transactions.CountAsync());
    }

    [Theory]
    [InlineData("UPDATE \"Accounts\" SET \"Balance\" = -1")]
    [InlineData("UPDATE \"Transactions\" SET \"Amount\" = 0")]
    [InlineData("UPDATE \"Transactions\" SET \"BalanceAfter\" = -1")]
    public async Task Database_rejects_invalid_money_even_when_the_domain_is_bypassed(string sql)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Contains("CHECK constraint failed", ex.Message);
    }

    [Fact]
    public async Task Concurrent_debits_cannot_both_succeed_and_the_loser_writes_nothing()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Accounts.SingleAsync(x => x.Name == "Checking");
        var b = await second.Accounts.SingleAsync(x => x.Name == "Checking");

        first.Transactions.Add(a.Debit(Money.Parse(100m), DateTimeOffset.UtcNow));
        await first.SaveChangesAsync();

        second.Transactions.Add(b.Debit(Money.Parse(100m), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = NewContext();
        var checking = await verify.Accounts.SingleAsync(x => x.Name == "Checking");
        var withdrawals = await verify.Transactions
            .CountAsync(t => t.AccountId == checking.Id && t.Type == TransactionType.Withdrawal);
        Assert.Equal(900m, checking.Balance);
        Assert.Equal(1, withdrawals);
    }

    [Fact]
    public async Task Saved_ledger_reconciles_with_the_balance_and_amounts_survive_exactly()
    {
        var at = DateTimeOffset.UtcNow;
        await using (var db = NewContext())
        {
            var savings = await db.Accounts.SingleAsync(x => x.Name == "Savings");
            db.Transactions.Add(savings.Credit(Money.Parse(0.01m), at.AddMinutes(1)));
            db.Transactions.Add(savings.Credit(Money.Parse(999_999_499.99m), at.AddMinutes(2)));
            db.Transactions.Add(savings.Debit(Money.Parse(0.33m), at.AddMinutes(3)));
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var account = await verify.Accounts.SingleAsync(x => x.Name == "Savings");
        var ledger = await verify.Transactions.Where(t => t.AccountId == account.Id).ToListAsync();

        var net = ledger.Sum(t => t.Type == TransactionType.Deposit ? t.Amount : -t.Amount);
        var latest = ledger.OrderByDescending(t => t.TimestampUtc).First();
        Assert.Equal(999_999_999.67m, account.Balance);
        Assert.Equal(account.Balance, net);
        Assert.Equal(account.Balance, latest.BalanceAfter);
    }

    [Fact]
    public async Task Sql_orders_transactions_newest_first_by_utc_instant()
    {
        var at = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var db = NewContext())
        {
            var checking = await db.Accounts.SingleAsync(x => x.Name == "Checking");
            db.Transactions.Add(checking.Credit(Money.Parse(2m), at.AddYears(5), description: "middle"));
            db.Transactions.Add(checking.Credit(Money.Parse(1m), at.AddYears(10), description: "newest"));
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var descriptions = await verify.Transactions
            .OrderByDescending(t => t.TimestampUtc)
            .Select(t => t.Description)
            .Take(2)
            .ToListAsync();

        Assert.Equal(["newest", "middle"], descriptions);
    }
}
