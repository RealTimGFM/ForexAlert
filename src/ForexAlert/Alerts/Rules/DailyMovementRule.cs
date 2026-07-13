using ForexAlert.Domain;
using ForexAlert.MarketData;
using ForexAlert.Scheduling;
using Microsoft.Extensions.Options;

namespace ForexAlert.Alerts.Rules;

public sealed class DailyMovementRule(
    IPriceStateStore prices,
    IPriceHistoryStore history,
    IMarketSchedule marketSchedule,
    IOptions<AlertOptions> alertOptions,
    IOptions<IbkrOptions> ibkrOptions) : AlertRuleBase(marketSchedule)
{
    public const string RuleName = "daily-movement";
    private static readonly TimeSpan DailyInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan MinuteInterval = TimeSpan.FromMinutes(1);
    private readonly AlertOptions _options = alertOptions.Value;
    private readonly TimeSpan _maximumQuoteAge = ibkrOptions.Value.QuoteStaleAfter;
    private readonly TimeSpan _maximumBidAskSkew = ibkrOptions.Value.MaximumBidAskSkew;

    public override string Name => RuleName;

    public override ValueTask<AlertCandidate?> EvaluateAsync(
        CurrencyPair pair,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!prices.TryGetMidPrice(
            pair,
            evaluationTimeUtc,
            _maximumQuoteAge,
            _maximumBidAskSkew,
            out PricePoint? current) || current is null)
        {
            return ValueTask.FromResult<AlertCandidate?>(null);
        }

        Candle? baselineCandle = _options.DailyBaseline == DailyBaselineKind.PreviousClose
            ? FindPreviousClose(pair, evaluationTimeUtc)
            : FindTradingDayOpen(pair, evaluationTimeUtc);
        if (baselineCandle is null)
        {
            return ValueTask.FromResult<AlertCandidate?>(null);
        }

        double baselinePrice = _options.DailyBaseline == DailyBaselineKind.PreviousClose
            ? baselineCandle.Close
            : baselineCandle.Open;
        DateTimeOffset baselineTimestamp = _options.DailyBaseline == DailyBaselineKind.PreviousClose
            ? baselineCandle.EndUtc
            : baselineCandle.StartUtc;
        double change = PriceMath.PercentageChange(baselinePrice, current.Price);
        bool sleepWindow = MarketSchedule.IsSleepWindow(evaluationTimeUtc);
        double threshold = sleepWindow
            ? _options.SleepWindowNegativeThresholdPercent
            : _options.DailyNormalThresholdPercent;
        bool triggered = sleepWindow
            ? change <= threshold
            : Math.Abs(change) >= threshold;

        return ValueTask.FromResult<AlertCandidate?>(triggered
            ? CreateCandidate(
                pair,
                baselineCandle,
                baselineTimestamp,
                baselinePrice,
                current.TimestampUtc,
                current.Price,
                change,
                threshold,
                evaluationTimeUtc)
            : null);
    }

    private Candle? FindPreviousClose(CurrencyPair pair, DateTimeOffset evaluationTimeUtc) =>
        history.GetCompletedCandles(pair, DailyInterval, evaluationTimeUtc, 1).LastOrDefault();

    private Candle? FindTradingDayOpen(CurrencyPair pair, DateTimeOffset evaluationTimeUtc)
    {
        DateTimeOffset expectedOpenUtc = MarketSchedule.GetTradingDayOpenUtc(evaluationTimeUtc);
        return history.GetCompletedCandles(pair, MinuteInterval, evaluationTimeUtc, 2_000)
            .FirstOrDefault(candle => candle.StartUtc == expectedOpenUtc);
    }
}
