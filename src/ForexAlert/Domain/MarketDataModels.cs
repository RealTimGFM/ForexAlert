namespace ForexAlert.Domain;

public enum QuoteSide
{
    Bid,
    Ask,
}

public sealed record QuoteUpdate(
    CurrencyPair Pair,
    QuoteSide Side,
    double Price,
    DateTimeOffset TimestampUtc);

public sealed record PricePoint(
    CurrencyPair Pair,
    double Price,
    DateTimeOffset TimestampUtc);

public sealed record QuoteSnapshot(
    CurrencyPair Pair,
    double Bid,
    DateTimeOffset BidTimestampUtc,
    double Ask,
    DateTimeOffset AskTimestampUtc)
{
    public double MidPrice => (Bid + Ask) / 2d;

    public DateTimeOffset TimestampUtc =>
        BidTimestampUtc >= AskTimestampUtc ? BidTimestampUtc : AskTimestampUtc;
}

public sealed record SubscriptionDescriptor(
    int RequestId,
    CurrencyPair RequestedPair,
    CurrencyPair ContractPair,
    bool IsInverse,
    MarketDataRequestKind Kind,
    TimeSpan? BarInterval = null);

public enum MarketDataRequestKind
{
    Streaming,
    Historical,
}
