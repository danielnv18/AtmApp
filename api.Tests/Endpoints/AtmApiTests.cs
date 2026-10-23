using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AtmApp.Api.Application;
using AtmApp.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmApp.Api.Tests.Endpoints;

public sealed class AtmApiTests : IAsyncLifetime
{
    private readonly AtmApiFactory _factory = new();
    private HttpClient _client = null!;
    private Guid _checking;
    private Guid _savings;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        var accounts = await _client.GetAsync<AccountView[]>("/api/atm/accounts");
        _checking = accounts.Single(a => a.Name == "Checking").Id;
        _savings = accounts.Single(a => a.Name == "Savings").Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private string Resolve(string template) => template
        .Replace("{checking}", _checking.ToString())
        .Replace("{savings}", _savings.ToString())
        .Replace("{unknown}", Guid.Empty.ToString());

    private Task<HttpResponseMessage> PostRawAsync(string url, string json) =>
        _client.PostAsync(url, new StringContent(Resolve(json), Encoding.UTF8, "application/json"));

    private Task<HttpResponseMessage> WithdrawAsync(Guid accountId, decimal amount) =>
        _client.PostAsJsonAsync("/api/atm/withdraw", new { accountId, amount });

    private async Task<decimal> BalanceAsync(Guid accountId) =>
        (await _client.GetAsync<AccountView[]>("/api/atm/accounts")).Single(a => a.Id == accountId).Balance;

    private async Task<IReadOnlyList<LedgerRowView>> LedgerAsync(Guid accountId) =>
        (await _client.GetAsync<HistoryView>($"/api/atm/accounts/{accountId}/history?limit=200")).Items;

    private static async Task<ProblemView> AssertProblemAsync(HttpResponseMessage response, int status, string title)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<ProblemView>();
        Assert.Equal((status, title), (problem.Status, problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        return problem;
    }

    [Fact]
    public async Task Accounts_lists_the_seeded_balances()
    {
        var accounts = await _client.GetAsync<AccountView[]>("/api/atm/accounts");

        Assert.Equal([("Checking", 1000.00m), ("Savings", 500.00m)], accounts.Select(a => (a.Name, a.Balance)));
    }

    [Fact]
    public async Task Money_is_always_written_with_two_decimals_and_type_as_a_string()
    {
        var accounts = await _client.GetStringAsync("/api/atm/accounts");
        var deposit = await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _checking, amount = 25m });
        var depositJson = await deposit.Content.ReadAsStringAsync();

        Assert.Contains("\"balance\":1000.00", accounts);
        Assert.Contains("\"type\":\"Deposit\"", depositJson);
        Assert.Contains("\"amount\":25.00", depositJson);
        Assert.Contains("\"balanceAfter\":1025.00", depositJson);
    }

    [Fact]
    public async Task Deposit_returns_the_ledger_row_stamped_by_the_clock_and_it_appears_first_in_history()
    {
        _factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var response = await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _checking, amount = 25.50m });
        var row = await response.ReadAsync<LedgerRowView>();
        var history = await _client.GetAsync<HistoryView>($"/api/atm/accounts/{_checking}/history?limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Deposit", 25.50m, 1025.50m), (row.Type, row.Amount, row.BalanceAfter));
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 12, 5, 0, TimeSpan.Zero), row.TimestampUtc);
        Assert.Equal(row.Id, Assert.Single(history.Items).Id);
    }

    [Fact]
    public async Task Transfer_returns_both_linked_rows_and_moves_the_money()
    {
        var response = await _client.PostAsJsonAsync("/api/atm/transfer",
            new { fromAccountId = _checking, toAccountId = _savings, amount = 100m });
        var receipt = await response.ReadAsync<TransferView>();
        var accounts = await _client.GetAsync<AccountView[]>("/api/atm/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("TransferOut", "TransferIn"), (receipt.TransferOut.Type, receipt.TransferIn.Type));
        Assert.Equal(receipt.CorrelationId, receipt.TransferOut.CorrelationId);
        Assert.Equal(receipt.CorrelationId, receipt.TransferIn.CorrelationId);
        Assert.Equal([900m, 600m], accounts.Select(a => a.Balance));
    }

    [Fact]
    public async Task Overdraw_returns_422_and_changes_neither_the_balance_nor_the_ledger()
    {
        var ledgerBefore = await LedgerAsync(_checking);

        var response = await WithdrawAsync(_checking, 1000.01m);

        await AssertProblemAsync(response, 422, "Insufficient funds");
        Assert.Equal(1000m, await BalanceAsync(_checking));
        Assert.Equal(ledgerBefore, await LedgerAsync(_checking));
    }

    [Fact]
    public async Task Withdrawing_the_exact_balance_leaves_zero()
    {
        var response = await WithdrawAsync(_checking, 1000m);
        var row = await response.ReadAsync<LedgerRowView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, row.BalanceAfter);
        Assert.Equal(0m, await BalanceAsync(_checking));
    }

    [Fact]
    public async Task Transfer_with_insufficient_funds_changes_neither_account_nor_the_ledger()
    {
        var checkingLedger = await LedgerAsync(_checking);
        var savingsLedger = await LedgerAsync(_savings);

        var response = await _client.PostAsJsonAsync("/api/atm/transfer",
            new { fromAccountId = _checking, toAccountId = _savings, amount = 1000.01m });

        await AssertProblemAsync(response, 422, "Insufficient funds");
        Assert.Equal((1000m, 500m), (await BalanceAsync(_checking), await BalanceAsync(_savings)));
        Assert.Equal(checkingLedger, await LedgerAsync(_checking));
        Assert.Equal(savingsLedger, await LedgerAsync(_savings));
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("0.10")]
    [InlineData("999999999.99")]
    public async Task Amounts_round_trip_exactly_over_http(string amount)
    {
        var response = await PostRawAsync("/api/atm/deposit", $$"""{"accountId":"{savings}","amount":{{amount}}}""");
        var json = await response.Content.ReadAsStringAsync();
        var history = await LedgerAsync(_savings);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"\"amount\":{amount}", json);
        Assert.Equal(decimal.Parse(amount), history[0].Amount);
        Assert.Equal(500m + decimal.Parse(amount), await BalanceAsync(_savings));
    }

    [Theory]
    [InlineData("/api/atm/deposit", """{"accountId":"{checking}","amount":AMOUNT}""")]
    [InlineData("/api/atm/withdraw", """{"accountId":"{checking}","amount":AMOUNT}""")]
    [InlineData("/api/atm/transfer", """{"fromAccountId":"{checking}","toAccountId":"{savings}","amount":AMOUNT}""")]
    public async Task Zero_negative_and_sub_cent_amounts_are_rejected_on_every_operation(string url, string body)
    {
        foreach (var amount in new[] { "0", "-5", "10.001" })
        {
            var response = await PostRawAsync(url, body.Replace("AMOUNT", amount));

            await AssertProblemAsync(response, 400, "Invalid amount");
        }

        Assert.Equal((1000m, 500m), (await BalanceAsync(_checking), await BalanceAsync(_savings)));
    }

    [Theory]
    [InlineData("/api/atm/deposit", "{")]
    [InlineData("/api/atm/deposit", "null")]
    [InlineData("/api/atm/deposit", """{"amount":5}""")]
    [InlineData("/api/atm/deposit", """{"accountId":"{checking}"}""")]
    [InlineData("/api/atm/deposit", """{"accountId":"{checking}","amount":"lots"}""")]
    [InlineData("/api/atm/withdraw", "{")]
    [InlineData("/api/atm/withdraw", "null")]
    [InlineData("/api/atm/withdraw", """{"amount":5}""")]
    [InlineData("/api/atm/withdraw", """{"accountId":"{checking}","amount":"lots"}""")]
    [InlineData("/api/atm/transfer", "{")]
    [InlineData("/api/atm/transfer", "null")]
    [InlineData("/api/atm/transfer", """{"fromAccountId":"{checking}","amount":5}""")]
    [InlineData("/api/atm/transfer", """{"fromAccountId":"{checking}","toAccountId":"{savings}","amount":"lots"}""")]
    public async Task Malformed_or_incomplete_bodies_are_400_problem_details(string url, string body)
    {
        var response = await PostRawAsync(url, body);

        await AssertProblemAsync(response, 400, "Bad request");
    }

    [Theory]
    [InlineData("POST", "/api/atm/transfer", """{"fromAccountId":"{checking}","toAccountId":"{checking}","amount":1}""", 400, "Same account transfer")]
    [InlineData("POST", "/api/atm/deposit", """{"accountId":"{unknown}","amount":1}""", 404, "Account not found")]
    [InlineData("GET", "/api/atm/accounts/{unknown}/history", null, 404, "Account not found")]
    [InlineData("POST", "/api/atm/withdraw", """{"accountId":"{savings}","amount":500.01}""", 422, "Insufficient funds")]
    [InlineData("GET", "/api/atm/accounts/{checking}/history?limit=abc", null, 400, "Bad request")]
    public async Task Failures_return_problem_details_with_the_mapped_status(
        string method, string url, string? body, int status, string title)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Resolve(url));
        if (body is not null)
        {
            request.Content = new StringContent(Resolve(body), Encoding.UTF8, "application/json");
        }

        await AssertProblemAsync(await _client.SendAsync(request), status, title);
    }

    [Fact]
    public async Task History_pages_follow_next_cursor_until_it_is_null()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _checking, amount = 1m });
        }

        var all = await _client.GetAsync<HistoryView>($"/api/atm/accounts/{_checking}/history?limit=200");
        var paged = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = $"/api/atm/accounts/{_checking}/history?limit=3" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await _client.GetAsync<HistoryView>(url);
            paged.AddRange(page.Items.Select(t => t.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Null(all.NextCursor);
        Assert.Equal(4, all.Items.Count);
        Assert.Equal(all.Items.Select(t => t.Id), paged);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("MTIzOmFiYw")]
    [InlineData("MA")]
    [InlineData("LTE")]
    [InlineData("A")]
    public async Task Tampered_cursor_is_a_400_with_title_and_detail(string cursor)
    {
        var response = await _client.GetAsync($"/api/atm/accounts/{_checking}/history?cursor={cursor}");

        await AssertProblemAsync(response, 400, "Invalid cursor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task History_limit_outside_1_to_200_is_a_400_with_title_and_detail(int limit)
    {
        var response = await _client.GetAsync($"/api/atm/accounts/{_checking}/history?limit={limit}");

        var problem = await AssertProblemAsync(response, 400, "Invalid limit");
        Assert.Equal("limit must be between 1 and 200.", problem.Detail);
    }

    [Fact]
    public async Task Parallel_withdrawals_never_overdraw_and_each_success_writes_exactly_one_ledger_row()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => WithdrawAsync(_checking, 100m))));

        var statuses = responses.Select(r => (int)r.StatusCode).ToList();
        var succeeded = statuses.Count(s => s == 200);
        var balance = await BalanceAsync(_checking);
        var withdrawals = (await LedgerAsync(_checking)).Count(t => t.Type == "Withdrawal");

        Assert.All(statuses, s => Assert.Contains(s, new[] { 200, 409, 422 }));
        Assert.True(balance >= 0, $"balance {balance} is negative");
        Assert.Equal(1000m - 100m * succeeded, balance);
        Assert.Equal(succeeded, withdrawals);
    }

    [Fact]
    public async Task Concurrent_opposite_transfers_conserve_money_and_keep_both_ledgers_consistent()
    {
        var requests = Enumerable.Range(0, 20).Select(i => i % 2 == 0
            ? (From: _checking, To: _savings)
            : (From: _savings, To: _checking));

        var responses = await Task.WhenAll(requests.Select(r => Task.Run(() =>
            _client.PostAsJsonAsync("/api/atm/transfer", new { fromAccountId = r.From, toAccountId = r.To, amount = 100m }))));

        Assert.All(responses, r => Assert.Contains((int)r.StatusCode, new[] { 200, 409, 422 }));
        var receipts = await Task.WhenAll(responses.Where(r => r.StatusCode == HttpStatusCode.OK).Select(r => r.ReadAsync<TransferView>()));
        var checkingBalance = await BalanceAsync(_checking);
        var savingsBalance = await BalanceAsync(_savings);
        var checkingLedger = await LedgerAsync(_checking);
        var savingsLedger = await LedgerAsync(_savings);

        Assert.Equal(1500.00m, checkingBalance + savingsBalance);
        AssertGaplessChain(checkingLedger, checkingBalance);
        AssertGaplessChain(savingsLedger, savingsBalance);
        var rows = checkingLedger.Concat(savingsLedger).ToList();
        Assert.All(receipts, receipt =>
        {
            var linked = rows.Where(t => t.CorrelationId == receipt.CorrelationId).ToList();
            Assert.Equal(["TransferIn", "TransferOut"], linked.Select(t => t.Type).Order());
            Assert.NotEqual(linked[0].AccountId, linked[1].AccountId);
        });
        Assert.Equal(receipts.Length * 2, rows.Count(t => t.CorrelationId is not null));
    }

    [Fact]
    public async Task Concurrent_deposits_each_write_one_row_and_the_balance_adds_up()
    {
        var amounts = Enumerable.Range(1, 20).Select(i => (decimal)i).ToList();

        var responses = await Task.WhenAll(amounts.Select(amount => Task.Run(async () =>
            (amount, response: await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _checking, amount })))));

        Assert.All(responses, r => Assert.Contains((int)r.response.StatusCode, new[] { 200, 409 }));
        var succeeded = responses.Where(r => r.response.StatusCode == HttpStatusCode.OK).Select(r => r.amount).ToList();
        var balance = await BalanceAsync(_checking);
        var ledger = await LedgerAsync(_checking);

        Assert.Equal(1000m + succeeded.Sum(), balance);
        Assert.Equal(succeeded.Order(), ledger.SkipLast(1).Select(t => t.Amount).Order());
        AssertGaplessChain(ledger, balance);
    }

    // Read newest-first over the wire: each row must extend the one before it by exactly its amount,
    // starting from the opening deposit, and end on the account's current balance.
    private static void AssertGaplessChain(IReadOnlyList<LedgerRowView> newestFirst, decimal balance)
    {
        var running = 0m;
        foreach (var row in newestFirst.Reverse())
        {
            running += row.Type is "Deposit" or "TransferIn" ? row.Amount : -row.Amount;
            Assert.Equal(running, row.BalanceAfter);
        }

        Assert.Equal(balance, newestFirst[0].BalanceAfter);
    }

    [Fact]
    public async Task Transfer_rows_carry_the_descriptions_the_client_displays()
    {
        var response = await _client.PostAsJsonAsync("/api/atm/transfer",
            new { fromAccountId = _checking, toAccountId = _savings, amount = 100m });
        var receipt = await response.ReadAsync<TransferView>();

        var withdrawal = (await LedgerAsync(_checking))[0];
        var deposit = (await LedgerAsync(_savings))[0];

        Assert.Equal((receipt.CorrelationId, "TransferOut", "Transfer to Savings"), (withdrawal.CorrelationId, withdrawal.Type, withdrawal.Description));
        Assert.Equal((receipt.CorrelationId, "TransferIn", "Transfer from Checking"), (deposit.CorrelationId, deposit.Type, deposit.Description));
    }

    [Fact]
    public async Task History_without_a_limit_returns_the_newest_50_rows_and_a_cursor()
    {
        LedgerRowView? newest = null;
        for (var i = 0; i < 50; i++)
        {
            newest = await (await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _checking, amount = 1m }))
                .ReadAsync<LedgerRowView>();
        }

        var page = await _client.GetAsync<HistoryView>($"/api/atm/accounts/{_checking}/history");
        var rest = await _client.GetAsync<HistoryView>($"/api/atm/accounts/{_checking}/history?cursor={page.NextCursor}");

        Assert.Equal(50, page.Items.Count);
        Assert.Equal(newest!.Id, page.Items[0].Id);
        Assert.NotNull(page.NextCursor);
        Assert.Equal("Opening deposit", Assert.Single(rest.Items).Description);
        Assert.Null(rest.NextCursor);
    }

    [Fact]
    public async Task History_contains_only_the_requested_accounts_rows()
    {
        var response = await _client.PostAsJsonAsync("/api/atm/deposit", new { accountId = _savings, amount = 7m });
        var deposit = await response.ReadAsync<LedgerRowView>();

        var checking = await LedgerAsync(_checking);
        var savings = await LedgerAsync(_savings);

        Assert.Contains(savings, t => t.Id == deposit.Id);
        Assert.DoesNotContain(checking, t => t.Id == deposit.Id);
        Assert.All(checking, t => Assert.Equal(_checking, t.AccountId));
        Assert.All(savings, t => Assert.Equal(_savings, t.AccountId));
    }

    [Fact]
    public async Task Unexpected_database_failure_returns_500_without_internals_and_leaves_the_balance()
    {
        await using (var connection = new SqliteConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE \"Transactions\"";
            await command.ExecuteNonQueryAsync();
        }

        var response = await WithdrawAsync(_checking, 100m);

        await AssertNoInternalsAsync(response, "Transactions");
        Assert.Equal(1000m, await BalanceAsync(_checking));
    }

    [Fact]
    public async Task Concurrent_update_maps_to_409_conflict()
    {
        await using var factory = new AtmApiFactory(configureServices: services =>
            services.AddScoped<IAccountService>(_ => new ThrowingService(new DbUpdateConcurrencyException("stale"))));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/atm/deposit", new { accountId = Guid.NewGuid(), amount = 1m });

        await AssertProblemAsync(response, 409, "Concurrent update");
    }

    // Development matters most: it is where framework error pages add exception details by default.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Unexpected_errors_return_500_without_leaking_internals(string environment)
    {
        await using var factory = new AtmApiFactory(environment, services =>
            services.AddScoped<IAccountService>(_ => new ThrowingService(new InvalidOperationException("secret connection detail"))));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/atm/deposit", new { accountId = Guid.NewGuid(), amount = 1m });

        await AssertNoInternalsAsync(response, "secret connection detail", "InvalidOperationException");
    }

    private static async Task AssertNoInternalsAsync(HttpResponseMessage response, params string[] secrets)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            ["detail", "status", "title", "traceId", "type"],
            json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("detail").GetString());
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, body));
        Assert.DoesNotContain(" at ", body);
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
