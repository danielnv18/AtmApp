using AtmApp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AtmApp.Api.Infrastructure;

public class AtmDbContext(DbContextOptions<AtmDbContext> options) : DbContext(options)
{
    // SQLite has no decimal type: EF would store decimals as TEXT, where CHECK constraints and
    // ORDER BY compare strings. Integer cents keep money exact and comparable in SQL.
    // Money guarantees at most 2 decimal places, so the conversion never loses a value.
    private static readonly ValueConverter<decimal, long> Cents = new(
        amount => decimal.ToInt64(decimal.Round(amount * 100m)),
        cents => cents / 100m);

    // EF stores DateTimeOffset as TEXT in SQLite, which can't be sorted or compared in SQL.
    // UTC ticks are a plain integer, so queries can order and filter by time inside the database.
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicks = new(
        timestamp => timestamp.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable(t => t.HasCheckConstraint("CK_Accounts_Balance", "\"Balance\" >= 0"));
            account.HasKey(a => a.Id);
            account.Property(a => a.Name).IsRequired().HasMaxLength(100);
            account.Property(a => a.Balance).HasConversion(Cents);
            // Updates include "WHERE LastSequence = <value we loaded>". Account advances the
            // sequence on every balance change, so a write based on a stale read matches zero rows
            // and fails instead of silently overwriting another request's balance.
            account.Property(a => a.LastSequence).IsConcurrencyToken();
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Transactions_Amount", "\"Amount\" > 0");
                t.HasCheckConstraint("CK_Transactions_BalanceAfter", "\"BalanceAfter\" >= 0");
            });
            transaction.HasKey(t => t.Id);
            transaction.Property(t => t.Amount).HasConversion(Cents);
            transaction.Property(t => t.BalanceAfter).HasConversion(Cents);
            transaction.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            transaction.Property(t => t.TimestampUtc).HasConversion(UtcTicks);
            transaction.Property(t => t.Description).HasMaxLength(200);
            // The ledger is the audit trail: deleting an account must never cascade away its history.
            transaction.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            // One row per position in each account's chain; also serves the history query in order.
            // The append-only and chain rules are triggers (see the AppendOnlyLedger migration). EF
            // orders one SaveChanges' inserts by key, not by Add order, so save at most one new row
            // per account per SaveChanges or the chain trigger may see them out of sequence.
            transaction.HasIndex(t => new { t.AccountId, t.Sequence }).IsUnique();
        });
    }
}
