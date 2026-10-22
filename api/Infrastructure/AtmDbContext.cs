using AtmApp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AtmApp.Api.Infrastructure;

public class AtmDbContext(DbContextOptions<AtmDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<decimal, long> Cents = new(
        amount => decimal.ToInt64(decimal.Round(amount * 100m)),
        cents => cents / 100m);

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
            account.Property(a => a.ConcurrencyVersion).IsConcurrencyToken();
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
            transaction.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            transaction.HasIndex(t => new { t.AccountId, t.TimestampUtc }).IsDescending(false, true);
        });
    }
}
