namespace AtmApp.Api.Domain;

// Proof that the operation named by Key already ran. Request is a canonical description of
// that operation, so a repeat can be told apart from a different request reusing the key.
// TransactionId is the operation's first ledger row (a transfer's TransferOut, whose
// CorrelationId leads to the TransferIn), from which the original response is rebuilt.
public sealed class IdempotencyRecord
{
    public string Key { get; private init; } = string.Empty;
    public string Request { get; private init; } = string.Empty;
    public Guid TransactionId { get; private init; }
    public DateTimeOffset CreatedUtc { get; private init; }

    // For EF Core materialization only; records are created through For.
    private IdempotencyRecord() { }

    public static IdempotencyRecord For(IdempotencyKey key, string request, Transaction firstRow, DateTimeOffset createdUtc) => new()
    {
        Key = key.Value,
        Request = request,
        TransactionId = firstRow.Id,
        CreatedUtc = createdUtc.ToUniversalTime(),
    };
}
