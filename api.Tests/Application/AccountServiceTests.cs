using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using AtmApp.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtmApp.Api.Tests.Application;

public sealed class AccountServiceTests : IAsyncLifetime
{
    // Later than the seed rows, which are stamped with the real clock, so new rows sort first.
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = new();
    private Guid _checking;
    private Guid _savings;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await using var db = _database.NewContext();
        _checking = (await db.Accounts.SingleAsync(a => a.Name == "Checking")).Id;
        _savings = (await db.Accounts.SingleAsync(a => a.Name == "Savings")).Id;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    private AccountService NewService(AtmDbContext db) => new(db, new FixedClock(Now));

    private async Task<(decimal Checking, decimal Savings, int LedgerRows)> SnapshotAsync()
    {
        await using var db = _database.NewContext();
        var balances = await db.Accounts.ToDictionaryAsync(a => a.Id, a => a.Balance);
        return (balances[_checking], balances[_savings], await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Deposit_and_withdraw_persist_the_new_balance_and_ledger_rows()
    {
        await using (var db = _database.NewContext())
        {
            var service = NewService(db);
            await service.DepositAsync(_checking, Money.Parse(50.25m));
            await service.WithdrawAsync(_checking, Money.Parse(0.25m));
        }

        Assert.Equal((1050.00m, 500.00m, 4), await SnapshotAsync());
    }

    [Fact]
    public async Task Transfer_moves_money_and_links_both_ledger_rows_with_one_correlation_id()
    {
        await using (var db = _database.NewContext())
        {
            await NewService(db).TransferAsync(_checking, _savings, Money.Parse(200m));
        }

        await using var verify = _database.NewContext();
        var linked = await verify.Transactions.Where(t => t.CorrelationId != null).ToListAsync();
        Assert.Equal((800m, 700m, 4), await SnapshotAsync());
        Assert.Equal(2, linked.Count);
        Assert.Single(linked.Select(t => t.CorrelationId).Distinct());
        Assert.Contains(linked, t => t.AccountId == _checking && t.Type == TransactionType.Withdrawal && t.BalanceAfter == 800m);
        Assert.Contains(linked, t => t.AccountId == _savings && t.Type == TransactionType.Deposit && t.BalanceAfter == 700m);
    }

    [Fact]
    public async Task Transfer_with_insufficient_funds_changes_nothing()
    {
        await using (var db = _database.NewContext())
        {
            var ex = await Assert.ThrowsAsync<DomainException>(
                () => NewService(db).TransferAsync(_savings, _checking, Money.Parse(500.01m)));
            Assert.Equal(DomainError.InsufficientFunds, ex.Error);
        }

        Assert.Equal((1000m, 500m, 2), await SnapshotAsync());
    }

    // EF picks the order of the two account updates. Failing each side in turn guarantees one
    // case fails after the other account's update has already run, which proves the rollback.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transfer_is_all_or_nothing_when_either_account_changed_underneath(bool editSource)
    {
        var editedAccount = editSource ? _checking : _savings;
        await using (var db = _database.NewContext(new ConcurrentEditTo(editedAccount)))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => NewService(db).TransferAsync(_checking, _savings, Money.Parse(100m)));
        }

        Assert.Equal((1000m, 500m, 2), await SnapshotAsync());
    }

    [Fact]
    public async Task Transfer_to_the_same_account_is_rejected()
    {
        await using var db = _database.NewContext();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => NewService(db).TransferAsync(_checking, _checking, Money.Parse(1m)));

        Assert.Equal(DomainError.SameAccountTransfer, ex.Error);
    }

    [Fact]
    public async Task Operations_on_an_unknown_account_fail_with_account_not_found()
    {
        await using var db = _database.NewContext();
        var service = NewService(db);
        var unknown = Guid.NewGuid();

        var history = await Assert.ThrowsAsync<DomainException>(() => service.GetHistoryAsync(unknown, 10));
        var transfer = await Assert.ThrowsAsync<DomainException>(
            () => service.TransferAsync(_checking, unknown, Money.Parse(1m)));

        Assert.Equal(DomainError.AccountNotFound, history.Error);
        Assert.Equal(DomainError.AccountNotFound, transfer.Error);
        Assert.Equal((1000m, 500m, 2), await SnapshotAsync());
    }

    [Fact]
    public async Task History_is_newest_first_stable_for_same_instant_rows_and_capped_by_limit()
    {
        await using var db = _database.NewContext();
        var service = NewService(db);
        for (var i = 0; i < 3; i++)
        {
            await service.DepositAsync(_checking, Money.Parse(1m));
        }

        var all = await service.GetHistoryAsync(_checking, 10);
        var firstTwo = await service.GetHistoryAsync(_checking, 2);
        var savingsHistory = await service.GetHistoryAsync(_savings, 10);

        Assert.Equal(4, all.Count);
        Assert.Equal(DatabaseInitializer.OpeningDescription, all[^1].Description);
        Assert.Equal(all.Take(2).Select(t => t.Id), firstTwo.Select(t => t.Id));
        Assert.All(savingsHistory, t => Assert.Equal(_savings, t.AccountId));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Simulates another request changing the account between our read and our commit.
    // EF stores Guids as uppercase TEXT in SQLite, hence ToUpperInvariant.
    private sealed class ConcurrentEditTo(Guid accountId) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var other = new SqliteConnection(eventData.Context!.Database.GetConnectionString());
            await other.OpenAsync(cancellationToken);
            await using var command = other.CreateCommand();
            command.CommandText = "UPDATE \"Accounts\" SET \"ConcurrencyVersion\" = $version WHERE \"Id\" = $id";
            command.Parameters.AddWithValue("$version", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$id", accountId.ToString().ToUpperInvariant());
            Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
            return result;
        }
    }
}
