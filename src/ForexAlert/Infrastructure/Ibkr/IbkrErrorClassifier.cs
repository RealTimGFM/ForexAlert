namespace ForexAlert.Infrastructure.Ibkr;

public static class IbkrErrorClassifier
{
    private static readonly HashSet<int> InformationalCodes =
    [
        1102,
        2103,
        2104,
        2105,
        2106,
        2107,
        2108,
        2158,
    ];

    public static bool IsInformational(int code) => InformationalCodes.Contains(code);

    public static bool IsMissingContract(int code) => code == 200;

    public static bool RequiresResubscription(int code) => code == 1101;

    public static bool IsConnectionLost(int code) => code == 1100;

    public static bool IsFarmConnectivityWarning(int code) => code is 2103 or 2105;
}
