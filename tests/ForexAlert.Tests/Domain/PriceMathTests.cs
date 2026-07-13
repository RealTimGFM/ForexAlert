using ForexAlert.Domain;

namespace ForexAlert.Tests.Domain;

public sealed class PriceMathTests
{
    [Theory]
    [InlineData(100d, 110d, 10d)]
    [InlineData(100d, 90d, -10d)]
    [InlineData(1.25d, 1.25d, 0d)]
    [InlineData(0.8d, 1d, 25d)]
    [InlineData(2d, 1d, -50d)]
    public void PercentageChange_ReturnsSignedPercentage(
        double baseline,
        double current,
        double expected)
    {
        double result = PriceMath.PercentageChange(baseline, current);

        Assert.Equal(expected, result, precision: 10);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void IsValid_RejectsNonPositiveOrNonFinitePrices(double value)
    {
        Assert.False(PriceMath.IsValid(value));
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(1d)]
    [InlineData(double.MaxValue)]
    public void IsValid_AcceptsPositiveFinitePrices(double value)
    {
        Assert.True(PriceMath.IsValid(value));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PercentageChange_RejectsInvalidBaseline(double baseline)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PriceMath.PercentageChange(baseline, 1d));

        Assert.Equal("baseline", exception.ParamName);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PercentageChange_RejectsInvalidCurrentPrice(double current)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PriceMath.PercentageChange(1d, current));

        Assert.Equal("current", exception.ParamName);
    }
}
