using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AtmApp.Api.Tests.Endpoints;

// Each factory runs the real app against its own SQLite file, so tests never touch the developer's atm.db.
public sealed class AtmApiFactory(string environment = "Development", Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atm-api-{Guid.NewGuid():N}.db");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public string ConnectionString => $"Data Source={_path};Default Timeout=5";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Atm", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            configureServices?.Invoke(services);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(_path + suffix);
        }
    }
}
