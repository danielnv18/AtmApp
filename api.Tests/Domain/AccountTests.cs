using AtmApp.Api.Domain;

namespace AtmApp.Api.Tests.Domain;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Debit_rejects_overdraft_and_leaves_account_untouched()
    {
        var account = Account.Open("checking", 50m);
        var versionBefore = account.ConcurrencyVersion;

        var ex = Assert.Throws<DomainException>(() => account.Debit(Money.Parse(50.01m), Now));

        Assert.Equal(DomainError.InsufficientFunds, ex.Error);
        Assert.Equal(50m, account.Balance);
        Assert.Equal(versionBefore, account.ConcurrencyVersion);
    }

    [Fact]
    public void Debit_updates_balance_rotates_version_and_records_balance_after()
    {
        var account = Account.Open("checking", 100m);
        var versionBefore = account.ConcurrencyVersion;

        var tx = account.Debit(Money.Parse(30.25m), Now);

        Assert.Equal(69.75m, account.Balance);
        Assert.NotEqual(versionBefore, account.ConcurrencyVersion);
        Assert.Equal(69.75m, tx.BalanceAfter);
        Assert.Equal(30.25m, tx.Amount);
        Assert.Equal(TransactionType.Withdrawal, tx.Type);
    }

    [Fact]
    public void Debit_of_exact_balance_is_allowed()
    {
        var account = Account.Open("checking", 25m);

        account.Debit(Money.Parse(25m), Now);

        Assert.Equal(0m, account.Balance);
    }
}
