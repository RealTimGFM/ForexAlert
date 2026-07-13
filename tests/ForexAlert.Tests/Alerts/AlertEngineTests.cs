using ForexAlert.Alerts;
using ForexAlert.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Alerts;

public sealed class AlertEngineTests
{
    private static readonly CurrencyPair EurUsd = CurrencyPair.Parse("EUR/USD");
    private static readonly CurrencyPair GbpUsd = CurrencyPair.Parse("GBP/USD");
    private static readonly DateTimeOffset EvaluationTime =
        new(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cooldown_SuppressesBeforeExpiryAndAllowsExactExpiry()
    {
        TimeSpan cooldown = TimeSpan.FromHours(1);
        RecordingAlertNotificationSender sender = new();
        InspectableAlertStateStore state = new();
        AlertEngine engine = CreateEngine(
            [new AlwaysCandidateAlertRule("rule-a")],
            sender,
            state,
            cooldown);

        IReadOnlyList<AlertCandidate> first = await engine.EvaluateAsync(
            [EurUsd],
            EvaluationTime,
            CancellationToken.None);
        IReadOnlyList<AlertCandidate> justBeforeExpiry = await engine.EvaluateAsync(
            [EurUsd],
            EvaluationTime.Add(cooldown).AddTicks(-1),
            CancellationToken.None);
        IReadOnlyList<AlertCandidate> atExpiry = await engine.EvaluateAsync(
            [EurUsd],
            EvaluationTime.Add(cooldown),
            CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(justBeforeExpiry);
        Assert.Single(atExpiry);
        Assert.Equal(2, sender.Alerts.Count);
        Assert.Equal(2, state.SetCount);
    }

    [Fact]
    public async Task CooldownState_IsKeyedIndependentlyByRuleAndCanonicalSymbol()
    {
        InspectableAlertStateStore state = new();
        state.Seed(new AlertKey("rule-a", EurUsd.CanonicalSymbol), EvaluationTime);
        RecordingAlertNotificationSender sender = new();
        AlertEngine engine = CreateEngine(
            [new AlwaysCandidateAlertRule("rule-a"), new AlwaysCandidateAlertRule("rule-b")],
            sender,
            state,
            TimeSpan.FromHours(1));

        IReadOnlyList<AlertCandidate> result = await engine.EvaluateAsync(
            [EurUsd, GbpUsd],
            EvaluationTime.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(
            result,
            alert => alert.RuleName == "rule-a" && alert.Pair == EurUsd);
        Assert.Contains(
            result,
            alert => alert.RuleName == "rule-b" && alert.Pair == EurUsd);
        Assert.Contains(
            result,
            alert => alert.RuleName == "rule-a" && alert.Pair == GbpUsd);
        Assert.Contains(
            result,
            alert => alert.RuleName == "rule-b" && alert.Pair == GbpUsd);
    }

    [Fact]
    public async Task ConcurrentEvaluations_DeliverAndPersistOnlyOnce()
    {
        RecordingAlertNotificationSender sender = new(
            async (_, cancellationToken) =>
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken));
        InspectableAlertStateStore state = new();
        AlertEngine engine = CreateEngine(
            [new AlwaysCandidateAlertRule("rule-a")],
            sender,
            state,
            TimeSpan.FromHours(1));

        Task<IReadOnlyList<AlertCandidate>>[] evaluations = Enumerable
            .Range(0, 16)
            .Select(_ => engine.EvaluateAsync([EurUsd], EvaluationTime, CancellationToken.None))
            .ToArray();
        IReadOnlyList<AlertCandidate>[] results = await Task.WhenAll(evaluations);

        Assert.Equal(1, results.Sum(result => result.Count));
        Assert.Single(sender.Alerts);
        Assert.Equal(1, state.SetCount);
    }

    [Fact]
    public async Task FailedNotification_IsNotPersistedAndCanBeRetried()
    {
        InspectableAlertStateStore state = new();
        RecordingAlertNotificationSender failingSender = new(
            (_, _) => Task.FromException(new InvalidOperationException("Simulated delivery failure.")));
        AlertEngine failingEngine = CreateEngine(
            [new AlwaysCandidateAlertRule("rule-a")],
            failingSender,
            state,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingEngine.EvaluateAsync(
            [EurUsd],
            EvaluationTime,
            CancellationToken.None));

        Assert.Equal(0, state.SetCount);
        AlertDeliveryState? persistedAfterFailure = await state.GetAsync(
            new AlertKey("rule-a", EurUsd.CanonicalSymbol),
            CancellationToken.None);
        Assert.Null(persistedAfterFailure);

        RecordingAlertNotificationSender retrySender = new();
        AlertEngine retryEngine = CreateEngine(
            [new AlwaysCandidateAlertRule("rule-a")],
            retrySender,
            state,
            TimeSpan.FromHours(1));
        IReadOnlyList<AlertCandidate> retry = await retryEngine.EvaluateAsync(
            [EurUsd],
            EvaluationTime,
            CancellationToken.None);

        Assert.Single(retry);
        Assert.Single(retrySender.Alerts);
        Assert.Equal(1, state.SetCount);
    }

    private static AlertEngine CreateEngine(
        IEnumerable<IAlertRule> rules,
        RecordingAlertNotificationSender sender,
        InspectableAlertStateStore state,
        TimeSpan cooldown) =>
        new(
            rules,
            sender,
            state,
            Options.Create(new AlertOptions { Cooldown = cooldown }),
            NullLogger<AlertEngine>.Instance);
}
