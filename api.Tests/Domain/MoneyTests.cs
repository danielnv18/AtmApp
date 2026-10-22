using AtmApp.Api.Domain;

namespace AtmApp.Api.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("0.001")]
    [InlineData("10.005")]
    [InlineData("1000000000.01")]
    public void Parse_rejects_amounts_the_ledger_cannot_represent(string raw)
    {
        var ex = Assert.Throws<DomainException>(() => Money.Parse(decimal.Parse(raw)));

        Assert.Equal(DomainError.InvalidAmount, ex.Error);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("10.5")]
    [InlineData("1000000000.00")]
    public void Parse_accepts_the_smallest_and_largest_valid_amounts(string raw)
    {
        var money = Money.Parse(decimal.Parse(raw));

        Assert.Equal(decimal.Parse(raw), money.Value);
    }
}
