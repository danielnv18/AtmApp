using System.Net;
using System.Net.Http.Json;
using System.Text;
using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using AtmApp.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmApp.Api.Tests.Endpoints;

public sealed class AtmApiTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atm-api-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _checking;
    private Guid _savings;

    public async Task InitializeAsync()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient();
        var accounts = await _client.GetFromJsonAsync<AccountResponse[]>("/api/atm/accounts");
        _checking = accounts!.Single(a => a.Name == "Checking").Id;
        _savings = accounts!.Single(a => a.Name == "Savings").Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        File.Delete(_path + "-journal");
    }

    // Points the app at a throwaway database so tests never touch the developer's atm.db.
    private WebApplicationFactory<Program> CreateFactory(IAccountService? serviceOverride = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Atm", $"Data Source={_path};Default Timeout=5");
            if (serviceOverride is not null)
            {
                host.ConfigureTestServices(services => services.AddScoped(_ => serviceOverride));
            }
        });

    private string Resolve(string template) => template
        .Replace("{checking}", _checking.ToString())
        .Replace("{savings}", _savings.ToString())
        .Replace("{unknown}", Guid.Empty.ToString());

    [Fact]
    public async Task Accounts_lists_the_seeded_balances()
    {
        var accounts = await _client.GetFromJsonAsync<AccountResponse[]>("/api/atm/accounts");

        Assert.Equal([("Checking", 1000.00m), ("Savings", 500.00m)], accounts!.Select(a => (a.Name, a.Balance)));
    }

    [Fact]
    public async Task Money_is_always_written_with_two_decimals()
    {
        var accounts = await _client.GetStringAsync("/api/atm/accounts");
        var deposit = await _client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 25m));
        var depositJson = await deposit.Content.ReadAsStringAsync();

        Assert.Contains("\"balance\":1000.00", accounts);
        Assert.Contains("\"amount\":25.00", depositJson);
        Assert.Contains("\"balanceAfter\":1025.00", depositJson);
    }

    [Fact]
    public async Task Deposit_returns_the_ledger_row_and_it_appears_first_in_history()
    {
        var response = await _client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 25.50m));
        var row = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        var history = await _client.GetFromJsonAsync<HistoryResponse>($"/api/atm/accounts/{_checking}/history?limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Deposit", 25.50m, 1025.50m), (row!.Type, row.Amount, row.BalanceAfter));
        Assert.Equal(row.Id, Assert.Single(history!.Items).Id);
    }

    [Fact]
    public async Task Transfer_returns_both_linked_rows_and_moves_the_money()
    {
        var response = await _client.PostAsJsonAsync("/api/atm/transfer", new TransferRequest(_checking, _savings, 100m));
        var receipt = await response.Content.ReadFromJsonAsync<TransferResponse>();
        var accounts = await _client.GetFromJsonAsync<AccountResponse[]>("/api/atm/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(receipt!.CorrelationId, receipt.Withdrawal.CorrelationId);
        Assert.Equal(receipt.CorrelationId, receipt.Deposit.CorrelationId);
        Assert.Equal([900m, 600m], accounts!.Select(a => a.Balance));
    }

    [Theory]
    [InlineData("POST", "/api/atm/deposit", """{"accountId":"{checking}","amount":10.005}""", 400, "Invalid amount")]
    [InlineData("POST", "/api/atm/deposit", """{"accountId":"{checking}","amount":0}""", 400, "Invalid amount")]
    [InlineData("POST", "/api/atm/transfer", """{"fromAccountId":"{checking}","toAccountId":"{checking}","amount":1}""", 400, "Same account transfer")]
    [InlineData("POST", "/api/atm/deposit", """{"accountId":"{unknown}","amount":1}""", 404, "Account not found")]
    [InlineData("GET", "/api/atm/accounts/{unknown}/history", null, 404, "Account not found")]
    [InlineData("POST", "/api/atm/withdraw", """{"accountId":"{savings}","amount":500.01}""", 422, "Insufficient funds")]
    [InlineData("POST", "/api/atm/deposit", """{"accountId":""", 400, "Bad request")]
    public async Task Failures_return_problem_details_with_the_mapped_status(
        string method, string url, string? body, int status, string title)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Resolve(url));
        if (body is not null)
        {
            request.Content = new StringContent(Resolve(body), Encoding.UTF8, "application/json");
        }

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(title, problem!.Title);
    }

    [Fact]
    public async Task Rejected_operations_do_not_change_any_balance()
    {
        await _client.PostAsJsonAsync("/api/atm/withdraw", new AmountRequest(_savings, 500.01m));
        await _client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 10.005m));
        await _client.PostAsJsonAsync("/api/atm/transfer", new TransferRequest(_savings, _checking, 9999m));

        var accounts = await _client.GetFromJsonAsync<AccountResponse[]>("/api/atm/accounts");
        Assert.Equal([1000m, 500m], accounts!.Select(a => a.Balance));
    }

    [Fact]
    public async Task History_pages_follow_next_cursor_until_it_is_null()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 1m));
        }

        var all = await _client.GetFromJsonAsync<HistoryResponse>($"/api/atm/accounts/{_checking}/history?limit=200");
        var paged = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = $"/api/atm/accounts/{_checking}/history?limit=3" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await _client.GetFromJsonAsync<HistoryResponse>(url);
            paged.AddRange(page!.Items.Select(t => t.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Null(all!.NextCursor);
        Assert.Equal(4, all.Items.Count);
        Assert.Equal(all.Items.Select(t => t.Id), paged);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("MTIzOmFiYw")]
    [InlineData("MA")]
    [InlineData("LTE")]
    public async Task Tampered_cursor_is_a_validation_error(string cursor)
    {
        var response = await _client.GetAsync($"/api/atm/accounts/{_checking}/history?cursor={cursor}");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cursor", problem!.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task History_limit_outside_1_to_200_is_a_validation_error(int limit)
    {
        var response = await _client.GetAsync($"/api/atm/accounts/{_checking}/history?limit={limit}");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("limit", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Concurrent_update_maps_to_409_conflict()
    {
        await using var factory = CreateFactory(new ThrowingService(new DbUpdateConcurrencyException("stale")));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 1m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unexpected_errors_return_500_without_leaking_internals()
    {
        await using var factory = CreateFactory(new ThrowingService(new InvalidOperationException("secret connection detail")));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/atm/deposit", new AmountRequest(_checking, 1m));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret connection detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    private sealed class ThrowingService(Exception exception) : IAccountService
    {
        public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default) => throw exception;
        public Task<HistoryPage> GetHistoryAsync(Guid accountId, int limit, HistoryCursor? after = null, CancellationToken ct = default) => throw exception;
        public Task<Transaction> DepositAsync(Guid accountId, Money amount, CancellationToken ct = default) => throw exception;
        public Task<Transaction> WithdrawAsync(Guid accountId, Money amount, CancellationToken ct = default) => throw exception;
        public Task<TransferReceipt> TransferAsync(Guid fromAccountId, Guid toAccountId, Money amount, CancellationToken ct = default) => throw exception;
    }
}
