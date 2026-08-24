using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.MarketData;

public sealed class MarketDataStateTests
{
    private static readonly CurrencyPair Pair = CurrencyPair.Parse("EUR/USD");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 1, 0, TimeSpan.Zero);

    [Fact]
    public void Midpoint_RequiresFreshSynchronizedBidAndAsk()
    {
        MarketDataState state = CreateState(staleAfter: TimeSpan.FromSeconds(10), skew: TimeSpan.FromSeconds(2));
        state.ProcessQuote(new(Pair, QuoteSide.Bid, 1.10, Now.AddSeconds(-2)));
        state.ProcessQuote(new(Pair, QuoteSide.Ask, 1.12, Now.AddSeconds(-1)));

        Assert.True(state.TryGetMidPrice(Pair, Now, out PricePoint? midpoint));
        Assert.Equal(1.11, midpoint!.Price, 10);
        Assert.False(state.HasFreshMidpoint(Pair, Now.AddSeconds(20)));

        state.ProcessQuote(new(Pair, QuoteSide.Bid, 1.11, Now.AddSeconds(30)));
        Assert.False(state.HasFreshMidpoint(Pair, Now.AddSeconds(30)));
    }

    [Fact]
    public void AdvanceTo_StoresOnlyCompletedCandles()
    {
        MarketDataState state = CreateState();
        DateTimeOffset start = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        state.ProcessQuote(new(Pair, QuoteSide.Bid, 1.00, start.AddSeconds(1)));
        state.ProcessQuote(new(Pair, QuoteSide.Ask, 1.02, start.AddSeconds(1)));

        Assert.Empty(state.AdvanceTo(start.AddSeconds(59)));
        Assert.Contains(state.AdvanceTo(start.AddMinutes(1)), candle => candle.Interval == TimeSpan.FromMinutes(1) && candle.Close == 1.01);
        Assert.Single(state.GetCompletedCandles(Pair, TimeSpan.FromMinutes(1), start.AddMinutes(1), 10));
    }

    [Fact]
    public void ResetAfterDisconnect_DropsQuotesAndPartialCandles()
    {
        MarketDataState state = CreateState();
        state.ProcessQuote(new(Pair, QuoteSide.Bid, 1.00, Now));
        state.ProcessQuote(new(Pair, QuoteSide.Ask, 1.02, Now));
        state.ResetAfterDisconnect(Now.AddSeconds(10));

        Assert.False(state.HasFreshMidpoint(Pair, Now.AddSeconds(10)));
        Assert.Empty(state.AdvanceTo(Now.AddMinutes(1)));
    }

    private static MarketDataState CreateState(TimeSpan? staleAfter = null, TimeSpan? skew = null) => new(
        Options.Create(new ForexAlertOptions { CurrencyPairs = ["EUR/USD"] }),
        Options.Create(new IbkrOptions { QuoteStaleAfter = staleAfter ?? TimeSpan.FromMinutes(1), MaximumBidAskSkew = skew ?? TimeSpan.FromSeconds(2) }),
        TimeProvider.System);
}
