using AtmApp.Api.Domain;

namespace AtmApp.Api.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.005)]
    public void Parse_rejects_invalid_amounts(double raw)
    {
        var ex = Assert.Throws<DomainException>(() => Money.Parse((decimal)raw));

        Assert.Equal(DomainError.InvalidAmount, ex.Error);
    }
}
