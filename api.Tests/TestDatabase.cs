using AtmApp.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtmApp.Api.Tests;

public sealed class TestDatabase : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atm-test-{Guid.NewGuid():N}.db");

    public AtmDbContext NewContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AtmDbContext>()
        .UseSqlite($"Data Source={_path};Default Timeout=5")
        .AddInterceptors(interceptors)
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
}
