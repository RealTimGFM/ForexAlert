using Microsoft.Extensions.Configuration;

namespace ForexAlert.Tests.Configuration;

[Collection("Process environment")]
public sealed class EnvironmentConfigurationTests
{
    [Fact]
    public void DotEnvValues_BindThroughPrefixedEnvironmentConfiguration()
    {
        const string thresholdKey = "FOREXALERT_ForexAlert__HourlyThresholdPercent";
        const string pairKey = "FOREXALERT_ForexAlert__CurrencyPairs__0";
        string filePath = Path.Combine(Path.GetTempPath(), $"ForexAlert-{Guid.NewGuid():N}.env");
        try
        {
            using EnvironmentScope environment = new((thresholdKey, null), (pairKey, null));
            File.WriteAllLines(filePath,
            [
                $"{thresholdKey}=2.25",
                $"{pairKey}=AUD/CAD",
            ]);

            DotNetEnv.Env.NoClobber().Load(filePath);
            IConfiguration configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables("FOREXALERT_")
                .Build();
            ForexAlertOptions options = configuration.GetSection("ForexAlert").Get<ForexAlertOptions>()!;

            Assert.Equal(2.25, options.HourlyThresholdPercent);
            Assert.Equal(["AUD/CAD"], options.CurrencyPairs);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void EnvironmentValues_BindThresholdsAndBooleans()
    {
        const string dailyKey = "FOREXALERT_ForexAlert__DailyThresholdPercent";
        const string sleepKey = "FOREXALERT_ForexAlert__SleepWindowNegativeThresholdPercent";
        const string enabledKey = "FOREXALERT_ForexAlert__OneMinuteEnabled";
        using EnvironmentScope environment = new(
            (dailyKey, "2.75"),
            (sleepKey, "-3.5"),
            (enabledKey, "true"));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables("FOREXALERT_")
            .Build();
        ForexAlertOptions options = configuration.GetSection("ForexAlert").Get<ForexAlertOptions>()!;

        Assert.Equal(2.75, options.DailyThresholdPercent);
        Assert.Equal(-3.5, options.SleepWindowNegativeThresholdPercent);
        Assert.True(options.OneMinuteEnabled);
    }

    [Fact]
    public void IndexedEnvironmentValues_BindCollectionsInOrder()
    {
        const string firstKey = "FOREXALERT_ForexAlert__CurrencyPairs__0";
        const string secondKey = "FOREXALERT_ForexAlert__CurrencyPairs__1";
        using EnvironmentScope environment = new(
            (firstKey, "EUR/USD"),
            (secondKey, "GBP/USD"));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables("FOREXALERT_")
            .Build();
        ForexAlertOptions options = configuration.GetSection("ForexAlert").Get<ForexAlertOptions>()!;

        Assert.Equal(["EUR/USD", "GBP/USD"], options.CurrencyPairs);
    }

    [Fact]
    public void NoClobber_PreservesOsEnvironmentValueOverDotEnvFile()
    {
        string variableName = $"FOREXALERT_Test__Override_{Guid.NewGuid():N}";
        string filePath = Path.Combine(Path.GetTempPath(), $"ForexAlert-{Guid.NewGuid():N}.env");
        try
        {
            File.WriteAllText(filePath, $"{variableName}=from-dotenv{Environment.NewLine}");
            using EnvironmentScope environment = new((variableName, "from-os"));

            DotNetEnv.Env.NoClobber().Load(filePath);

            Assert.Equal("from-os", Environment.GetEnvironmentVariable(variableName));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly (string Name, string? OriginalValue)[] _originalValues;

        public EnvironmentScope(params (string Name, string? Value)[] values)
        {
            _originalValues = values
                .Select(value => (value.Name, Environment.GetEnvironmentVariable(value.Name)))
                .ToArray();
            foreach ((string name, string? value) in values)
                Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach ((string name, string? originalValue) in _originalValues)
                Environment.SetEnvironmentVariable(name, originalValue);
        }
    }
}

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
