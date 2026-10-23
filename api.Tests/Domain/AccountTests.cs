using AtmApp.Api.Domain;

namespace AtmApp.Api.Tests.Domain;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Account Funded(decimal opening)
    {
        var account = Account.Open("checking");
        account.Deposit(Money.Parse(opening), Now);
        return account;
    }

    [Fact]
    public void A_new_account_is_empty_with_no_ledger_rows()
    {
        var account = Account.Open("checking");

        Assert.Equal(0m, account.Balance);
        Assert.Equal(0, account.LastSequence);
    }

    [Fact]
    public void Credit_increases_balance_and_records_a_deposit_at_the_next_sequence()
    {
        var account = Funded(100m);

        var tx = account.Deposit(Money.Parse(0.10m), Now);

        Assert.Equal(100.10m, account.Balance);
        Assert.Equal(2, account.LastSequence);
        Assert.Equal(2, tx.Sequence);
        Assert.Equal(TransactionType.Deposit, tx.Type);
        Assert.Equal(0.10m, tx.Amount);
        Assert.Equal(100.10m, tx.BalanceAfter);
    }

    [Fact]
    public void Debit_decreases_balance_and_records_a_withdrawal_at_the_next_sequence()
    {
        var account = Funded(100m);

        var tx = account.Withdraw(Money.Parse(30.25m), Now);

        Assert.Equal(69.75m, account.Balance);
        Assert.Equal(2, account.LastSequence);
        Assert.Equal(2, tx.Sequence);
        Assert.Equal(TransactionType.Withdrawal, tx.Type);
        Assert.Equal(30.25m, tx.Amount);
        Assert.Equal(69.75m, tx.BalanceAfter);
    }

    [Fact]
    public void Debit_rejects_overdraft_and_leaves_account_untouched()
    {
        var account = Funded(50m);

        var ex = Assert.Throws<DomainException>(() => account.Withdraw(Money.Parse(50.01m), Now));

        Assert.Equal(DomainError.InsufficientFunds, ex.Error);
        Assert.Equal(50m, account.Balance);
        Assert.Equal(1, account.LastSequence);
    }

    [Fact]
    public void Debit_of_the_exact_balance_is_allowed_and_leaves_zero()
    {
        var account = Funded(25m);

        var tx = account.Withdraw(Money.Parse(25m), Now);

        Assert.Equal(0m, account.Balance);
        Assert.Equal(0m, tx.BalanceAfter);
    }

    [Fact]
    public void Transaction_is_a_faithful_audit_record_with_a_utc_timestamp()
    {
        var account = Funded(100m);
        var correlationId = Guid.NewGuid();
        var localTime = new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.FromHours(-5));

        var tx = account.SendTransfer(Money.Parse(10m), localTime, correlationId, "ATM withdrawal");

        Assert.NotEqual(Guid.Empty, tx.Id);
        Assert.Equal(account.Id, tx.AccountId);
        Assert.Equal(TransactionType.TransferOut, tx.Type);
        Assert.Equal(correlationId, tx.CorrelationId);
        Assert.Equal("ATM withdrawal", tx.Description);
        Assert.Equal(localTime, tx.TimestampUtc);
        Assert.Equal(TimeSpan.Zero, tx.TimestampUtc.Offset);
    }

    [Fact]
    public void Each_transaction_records_the_running_balance_with_no_cent_drift()
    {
        var account = Account.Open("checking");

        var ledger = new List<Transaction>
        {
            account.Deposit(Money.Parse(100m), Now),
            account.Withdraw(Money.Parse(33.33m), Now),
            account.Deposit(Money.Parse(0.01m), Now),
            account.Withdraw(Money.Parse(66.68m), Now),
        };

        Assert.Equal(0m, account.Balance);
        Assert.Equal([100m, 66.67m, 66.68m, 0m], ledger.Select(t => t.BalanceAfter));
        Assert.Equal([1L, 2L, 3L, 4L], ledger.Select(t => t.Sequence));
    }
}
