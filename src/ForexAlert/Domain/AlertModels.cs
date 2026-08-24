namespace ForexAlert.Domain;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public enum AlertDirection
{
    Down,
    Up,
}

public sealed record AlertCandidate(
    string RuleName,
    CurrencyPair Pair,
    DateTimeOffset BaselineTimestampUtc,
    double BaselinePrice,
    DateTimeOffset CurrentTimestampUtc,
    double CurrentPrice,
    double PercentageChange,
    AlertDirection Direction,
    double TriggerThresholdPercent,
    DateTimeOffset EvaluationTimeUtc,
    string MarketLocalTime)
{
    public string CanonicalSymbol => Pair.CanonicalSymbol;

    public string DeliveryFingerprint
    {
        get
        {
            string value = string.Join(
                '|',
                RuleName,
                CanonicalSymbol,
                BaselineTimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
                BaselinePrice.ToString("R", CultureInfo.InvariantCulture),
                CurrentTimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
                CurrentPrice.ToString("R", CultureInfo.InvariantCulture),
                TriggerThresholdPercent.ToString("R", CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}

public sealed record AlertDeliveryState(
    DateTimeOffset LastSentUtc,
    string CandidateFingerprint);
