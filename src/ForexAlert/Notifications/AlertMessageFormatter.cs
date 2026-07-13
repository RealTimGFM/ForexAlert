using System.Globalization;
using ForexAlert.Domain;

namespace ForexAlert.Notifications;

public static class AlertMessageFormatter
{
    public static string Format(AlertCandidate alert) => string.Format(
        CultureInfo.InvariantCulture,
        "ForexAlert {0}: {1} moved {2:+0.0000;-0.0000;0.0000}% from {3:0.#####} at {4:O} to {5:0.#####} at {6:O}. Threshold: {7:+0.####;-0.####;0.####}%. Market time: {8}.",
        alert.RuleName,
        alert.CanonicalSymbol,
        alert.PercentageChange,
        alert.BaselinePrice,
        alert.BaselineTimestampUtc,
        alert.CurrentPrice,
        alert.CurrentTimestampUtc,
        alert.TriggerThresholdPercent,
        alert.MarketLocalTime);
}
