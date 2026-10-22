namespace AtmApp.Api.Domain;

public readonly record struct Money
{
    public const decimal MaxValue = 1_000_000_000.00m;

    public decimal Value { get; }

    private Money(decimal value) => Value = value;

    public static Money Parse(decimal value)
    {
        if (value <= 0 || value > MaxValue || decimal.Round(value, 2) != value)
        {
            throw new DomainException(DomainError.InvalidAmount);
        }

        return new Money(value);
    }
}
