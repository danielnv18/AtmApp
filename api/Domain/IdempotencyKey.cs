using System.Diagnostics.CodeAnalysis;

namespace AtmApp.Api.Domain;

// A client's name for one intended operation. Sending it again asks for the same operation,
// not a new one.
public readonly record struct IdempotencyKey
{
    public const int MaxLength = 100;

    public string Value { get; }

    private IdempotencyKey(string value) => Value = value;

    public static bool TryParse([NotNullWhen(true)] string? text, out IdempotencyKey key)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            key = default;
            return false;
        }

        key = new IdempotencyKey(trimmed);
        return true;
    }
}
