using System.Net.Http.Json;
using System.Text.Json;

namespace AtmApp.Api.Tests.Endpoints;

// The wire contract as a client sees it. These are deliberately not the API's own types, so a
// renamed, retyped, or removed field fails to deserialize instead of following the API silently.
public sealed record AccountView(Guid Id, string Name, decimal Balance);

public sealed record LedgerRowView(
    Guid Id,
    Guid AccountId,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? CorrelationId,
    DateTimeOffset TimestampUtc,
    string? Description);

public sealed record HistoryView(IReadOnlyList<LedgerRowView> Items, string? NextCursor);

public sealed record TransferView(Guid CorrelationId, LedgerRowView TransferOut, LedgerRowView TransferIn);

public sealed record ProblemView(int Status, string Title, string Detail);

public static class ApiContract
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Strict))!;

    public static async Task<T> GetAsync<T>(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<T>();
    }
}
