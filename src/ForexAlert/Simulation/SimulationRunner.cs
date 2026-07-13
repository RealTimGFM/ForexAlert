using System.Text.Json;
using ForexAlert.Alerts;
using ForexAlert.Domain;
using ForexAlert.MarketData;
using Microsoft.Extensions.Logging;

namespace ForexAlert.Simulation;

public sealed class SimulationRunner(
    IMarketDataPipeline pipeline,
    IAlertEngine alertEngine,
    ConfiguredCurrencyPairs configuredPairs,
    ILogger<SimulationRunner> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<int> RunAsync(string fixtureArgument, CancellationToken cancellationToken)
    {
        string path = ResolveFixturePath(fixtureArgument);
        await using FileStream stream = File.OpenRead(path);
        SimulationFixture fixture = await JsonSerializer
            .DeserializeAsync<SimulationFixture>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Simulation fixture '{path}' is empty.");
        if (fixture.EvaluateAtUtc == default)
        {
            throw new InvalidDataException("Simulation fixture must define EvaluateAtUtc.");
        }

        foreach (SimulationEvent simulationEvent in fixture.Events.OrderBy(static item => item.TimestampUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessEvent(simulationEvent);
            pipeline.AdvanceTo(simulationEvent.TimestampUtc);
        }

        pipeline.AdvanceTo(fixture.EvaluateAtUtc);
        IReadOnlyList<AlertCandidate> alerts = await alertEngine
            .EvaluateAsync(configuredPairs.Values, fixture.EvaluateAtUtc, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Simulation {SimulationName} completed from {FixturePath} and emitted {AlertCount} alert(s)",
            fixture.Name,
            path,
            alerts.Count);
        return alerts.Count;
    }

    private void ProcessEvent(SimulationEvent simulationEvent)
    {
        CurrencyPair pair = CurrencyPair.Parse(simulationEvent.Symbol);
        if (string.Equals(simulationEvent.Type, "quote", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse(simulationEvent.Side, ignoreCase: true, out QuoteSide side) || !simulationEvent.Price.HasValue)
            {
                throw new InvalidDataException("Quote events require Side and Price.");
            }

            pipeline.ProcessQuote(new QuoteUpdate(pair, side, simulationEvent.Price.Value, simulationEvent.TimestampUtc));
            return;
        }

        if (string.Equals(simulationEvent.Type, "candle", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseInterval(simulationEvent.Interval, out TimeSpan interval) ||
                !simulationEvent.Open.HasValue || !simulationEvent.High.HasValue ||
                !simulationEvent.Low.HasValue || !simulationEvent.Close.HasValue)
            {
                throw new InvalidDataException("Candle events require Interval and complete OHLC values.");
            }

            pipeline.ProcessHistoricalCandle(new Candle(
                pair,
                simulationEvent.TimestampUtc.ToUniversalTime(),
                interval,
                simulationEvent.Open.Value,
                simulationEvent.High.Value,
                simulationEvent.Low.Value,
                simulationEvent.Close.Value));
            return;
        }

        throw new InvalidDataException($"Unsupported simulation event type '{simulationEvent.Type}'.");
    }

    private static bool TryParseInterval(string? value, out TimeSpan interval)
    {
        interval = value?.ToLowerInvariant() switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "1h" => TimeSpan.FromHours(1),
            "1d" => TimeSpan.FromDays(1),
            _ => default,
        };
        return interval > TimeSpan.Zero;
    }

    private static string ResolveFixturePath(string fixtureArgument)
    {
        string direct = Path.GetFullPath(fixtureArgument);
        if (File.Exists(direct))
        {
            return direct;
        }

        string fileName = Path.HasExtension(fixtureArgument) ? fixtureArgument : $"{fixtureArgument}.json";
        string bundled = Path.Combine(AppContext.BaseDirectory, "Simulation", "Fixtures", fileName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        throw new FileNotFoundException(
            $"Simulation fixture '{fixtureArgument}' was not found as a path or bundled fixture.");
    }
}
