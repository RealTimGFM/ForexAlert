using ForexAlert.Domain;

namespace ForexAlert.Tests.Domain;

public sealed class CurrencyPairTests
{
    [Theory]
    [InlineData("EUR/USD", "EUR", "USD")]
    [InlineData(" eur/usd ", "EUR", "USD")]
    [InlineData("gBp/jPy", "GBP", "JPY")]
    public void Parse_NormalizesValidPair(
        string value,
        string expectedBaseCurrency,
        string expectedQuoteCurrency)
    {
        CurrencyPair pair = CurrencyPair.Parse(value);

        Assert.Equal(expectedBaseCurrency, pair.BaseCurrency);
        Assert.Equal(expectedQuoteCurrency, pair.QuoteCurrency);
        Assert.Equal($"{expectedBaseCurrency}/{expectedQuoteCurrency}", pair.CanonicalSymbol);
        Assert.Equal(pair.CanonicalSymbol, pair.ToString());
    }

    [Fact]
    public void TryParse_ReturnsNormalizedPairForValidInput()
    {
        bool parsed = CurrencyPair.TryParse("  cad/chf  ", out CurrencyPair pair);

        Assert.True(parsed);
        Assert.Equal(CurrencyPair.Parse("CAD/CHF"), pair);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EURUSD")]
    [InlineData("EUR-USD")]
    [InlineData("EUR/USD/JPY")]
    [InlineData("EU/USD")]
    [InlineData("EURO/USD")]
    [InlineData("E1R/USD")]
    [InlineData("ÉUR/USD")]
    [InlineData("EUR/US$")]
    [InlineData("USD/USD")]
    [InlineData("EUR /USD")]
    public void TryParse_RejectsMalformedPair(string? value)
    {
        bool parsed = CurrencyPair.TryParse(value, out CurrencyPair pair);

        Assert.False(parsed);
        Assert.Equal(default, pair);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EURUSD")]
    [InlineData("USD/USD")]
    public void Parse_ThrowsFormatExceptionForInvalidInput(string? value)
    {
        Assert.Throws<FormatException>(() => CurrencyPair.Parse(value!));
    }

    [Fact]
    public void Inverse_SwapsCurrenciesAndRoundTrips()
    {
        CurrencyPair pair = CurrencyPair.Parse("EUR/USD");

        CurrencyPair inverse = pair.Inverse();

        Assert.Equal("USD/EUR", inverse.CanonicalSymbol);
        Assert.Equal(pair, inverse.Inverse());
        Assert.NotEqual(pair, inverse);
    }

    [Fact]
    public void Equality_IsValueBasedAndSupportsOperators()
    {
        CurrencyPair first = CurrencyPair.Parse("eur/usd");
        CurrencyPair equal = CurrencyPair.Parse("EUR/USD");
        CurrencyPair different = CurrencyPair.Parse("EUR/GBP");

        Assert.True(first.Equals(equal));
        Assert.True(first.Equals((object)equal));
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.True(first == equal);
        Assert.False(first != equal);
        Assert.False(first == different);
        Assert.True(first != different);
        Assert.False(first.Equals("EUR/USD"));
    }
}
