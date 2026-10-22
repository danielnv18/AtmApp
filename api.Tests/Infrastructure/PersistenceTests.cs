using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AtmApp.Api.Tests.Infrastructure;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    private AtmDbContext NewContext() => _database.NewContext();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

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
            Assert.Equal(1, t.Sequence);
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

    [Fact]
    public async Task Database_rejects_a_negative_balance_even_when_the_domain_is_bypassed()
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<SqliteException>(
            () => db.Database.ExecuteSqlRawAsync("UPDATE \"Accounts\" SET \"Balance\" = -1"));

        Assert.Contains("CHECK constraint failed", ex.Message);
    }

    [Theory]
    [InlineData("UPDATE \"Transactions\" SET \"Description\" = 'edited'")]
    [InlineData("UPDATE \"Transactions\" SET \"Amount\" = 0")]
    [InlineData("DELETE FROM \"Transactions\"")]
    public async Task Database_rejects_any_change_to_an_existing_ledger_row(string sql)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Contains("Ledger is append-only", ex.Message);
        Assert.Equal(2, await db.Transactions.CountAsync(t => t.Description == DatabaseInitializer.OpeningDescription));
    }

    // Checking is seeded with one row: sequence 1, balance after 1000.00 (100000 cents).
    [Theory]
    [InlineData(3, 100, 100100, "Deposit", "Ledger chain broken")]
    [InlineData(1, 100, 100100, "Deposit", "Ledger chain broken")]
    [InlineData(2, 100, 100200, "Deposit", "Ledger chain broken")]
    [InlineData(2, 100, 100100, "Withdrawal", "Ledger chain broken")]
    [InlineData(2, 100, 100100, "Bonus", "Ledger chain broken")]
    [InlineData(2, 0, 100000, "Deposit", "CHECK constraint failed")]
    [InlineData(2, 100001, -1, "Withdrawal", "CHECK constraint failed")]
    public async Task Database_rejects_a_ledger_row_that_does_not_extend_the_chain(
        long sequence, long amountCents, long balanceAfterCents, string type, string error)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(
            () => InsertCheckingRowAsync(sequence, amountCents, balanceAfterCents, type));

        Assert.Contains(error, ex.Message);
    }

    [Fact]
    public async Task Database_accepts_a_raw_ledger_row_that_extends_the_chain()
    {
        await InsertCheckingRowAsync(2, 2500, 97500, "Withdrawal");

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Transactions.CountAsync(t => t.Sequence == 2 && t.BalanceAfter == 975m));
    }

    private async Task InsertCheckingRowAsync(long sequence, long amountCents, long balanceAfterCents, string type)
    {
        await using var db = NewContext();
        var checking = await db.Accounts.SingleAsync(a => a.Name == "Checking");
        // EF stores Guids as uppercase TEXT in SQLite; the FK compares the text exactly.
        var id = Guid.NewGuid().ToString().ToUpperInvariant();
        var accountId = checking.Id.ToString().ToUpperInvariant();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Sequence", "Amount", "BalanceAfter", "Type", "TimestampUtc")
            VALUES ({id}, {accountId}, {sequence}, {amountCents}, {balanceAfterCents}, {type}, 0)
            """);
    }

    [Fact]
    public async Task Migrating_an_existing_database_numbers_each_ledger_and_installs_the_guards()
    {
        var legacy = new TestDatabase();
        try
        {
            var (a, b) = ("A0000000-0000-0000-0000-000000000000", "B0000000-0000-0000-0000-000000000000");
            await using (var db = legacy.NewContext())
            {
                await db.GetService<IMigrator>().MigrateAsync("20261022215308_InitialCreate");
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "Accounts" ("Id", "Name", "Balance", "ConcurrencyVersion") VALUES
                        ('A0000000-0000-0000-0000-000000000000', 'A', 7500, 'C0000000-0000-0000-0000-000000000000'),
                        ('B0000000-0000-0000-0000-000000000000', 'B', 5000, 'C0000000-0000-0000-0000-000000000000');
                    INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "BalanceAfter", "Type", "TimestampUtc", "Description") VALUES
                        ('00000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000000', 3000, 7000, 'Withdrawal', 20, 'a2'),
                        ('00000000-0000-0000-0000-000000000002', 'A0000000-0000-0000-0000-000000000000', 10000, 10000, 'Deposit', 10, 'a1'),
                        ('00000000-0000-0000-0000-000000000003', 'B0000000-0000-0000-0000-000000000000', 5000, 5000, 'Deposit', 20, 'b1'),
                        ('00000000-0000-0000-0000-000000000004', 'A0000000-0000-0000-0000-000000000000', 500, 7500, 'Deposit', 20, 'a3');
                    """);
            }

            await using (var db = legacy.NewContext())
            {
                await db.Database.MigrateAsync();
            }

            await using var verify = legacy.NewContext();
            var ledger = await verify.Transactions
                .OrderBy(t => t.AccountId).ThenBy(t => t.Sequence)
                .Select(t => new { t.Description, t.Sequence })
                .ToListAsync();
            var lastSequences = await verify.Accounts.OrderBy(x => x.Name).Select(x => x.LastSequence).ToListAsync();
            var triggers = await verify.Database
                .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'trigger' ORDER BY name")
                .ToListAsync();

            Assert.Equal(
                [("a1", 1L), ("a2", 2L), ("a3", 3L), ("b1", 1L)],
                ledger.Select(t => (t.Description!, t.Sequence)));
            Assert.Equal([3L, 1L], lastSequences);
            Assert.Equal(["TR_Transactions_Chain", "TR_Transactions_NoDelete", "TR_Transactions_NoUpdate"], triggers);

            var next = await new AccountService(verify, TimeProvider.System).WithdrawAsync(Guid.Parse(a), Money.Parse(75m));
            Assert.Equal((4L, 0m), (next.Sequence, next.BalanceAfter));
            Assert.Equal("b1", (await verify.Transactions.SingleAsync(t => t.AccountId == Guid.Parse(b))).Description);
        }
        finally
        {
            await legacy.DisposeAsync();
        }
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
            await db.SaveChangesAsync();
            db.Transactions.Add(savings.Credit(Money.Parse(999_999_499.99m), at.AddMinutes(2)));
            await db.SaveChangesAsync();
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
            await db.SaveChangesAsync();
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
