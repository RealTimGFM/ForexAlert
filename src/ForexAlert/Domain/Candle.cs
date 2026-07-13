namespace ForexAlert.Domain;

public sealed record Candle
{
    public Candle(
        CurrencyPair pair,
        DateTimeOffset startUtc,
        TimeSpan interval,
        double open,
        double high,
        double low,
        double close)
    {
        if (startUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Candle timestamps must use UTC.", nameof(startUtc));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (!new[] { open, high, low, close }.All(PriceMath.IsValid))
        {
            throw new ArgumentOutOfRangeException(nameof(open), "OHLC prices must be finite and positive.");
        }

        if (high < Math.Max(open, close) || low > Math.Min(open, close) || high < low)
        {
            throw new ArgumentException("Candle high/low values do not contain the open and close.");
        }

        Pair = pair;
        StartUtc = startUtc;
        Interval = interval;
        Open = open;
        High = high;
        Low = low;
        Close = close;
    }

    public CurrencyPair Pair { get; }

    public DateTimeOffset StartUtc { get; }

    public TimeSpan Interval { get; }

    public double Open { get; }

    public double High { get; }

    public double Low { get; }

    public double Close { get; }

    public DateTimeOffset EndUtc => StartUtc.Add(Interval);
}

public static class PriceMath
{
    public static bool IsValid(double price) => double.IsFinite(price) && price > 0d;

    public static double PercentageChange(double baseline, double current)
    {
        if (!IsValid(baseline))
        {
            throw new ArgumentOutOfRangeException(nameof(baseline), "Baseline must be finite and positive.");
        }

        if (!IsValid(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current), "Current price must be finite and positive.");
        }

        return ((current - baseline) / baseline) * 100d;
    }
}
