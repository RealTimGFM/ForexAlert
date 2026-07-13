using System.Text.Json;
using ForexAlert.Domain;
using ForexAlert.Persistence;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Persistence;

public sealed class JsonAlertStateStoreTests
{
    [Fact]
    public async Task GetLastSentAsync_MissingFile_ReturnsNullWithoutCreatingState()
    {
        string directory = CreateIsolatedDirectory();
        try
        {
            string path = Path.Combine(directory, "missing", "alert-state.json");
            JsonAlertStateStore store = CreateStore(path);

            AlertDeliveryState? value = await store.GetAsync(
                new AlertKey("daily-movement", "EUR/USD"),
                CancellationToken.None);

            Assert.Null(value);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetLastSentAsync_RoundTripsUtcTimestampAndAtomicJson()
    {
        string directory = CreateIsolatedDirectory();
        try
        {
            string path = Path.Combine(directory, "nested", "alert-state.json");
            JsonAlertStateStore store = CreateStore(path);
            AlertKey key = new("daily-movement", "EUR/USD");
            DateTimeOffset localTimestamp = new(2026, 7, 1, 8, 30, 0, TimeSpan.FromHours(-4));

            AlertDeliveryState state = new(localTimestamp, "candidate-a");
            await store.SetAsync(key, state, CancellationToken.None);
            AlertDeliveryState? reloaded = await store.GetAsync(key, CancellationToken.None);

            Assert.Equal(localTimestamp.ToUniversalTime(), reloaded?.LastSentUtc);
            Assert.Equal("candidate-a", reloaded?.CandidateFingerprint);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists($"{path}.{Environment.ProcessId}.tmp"));
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.True(document.RootElement.TryGetProperty("daily-movement|EUR/USD", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewStoreInstance_ReloadsPreviouslyPersistedCooldown()
    {
        string directory = CreateIsolatedDirectory();
        try
        {
            string path = Path.Combine(directory, "alert-state.json");
            AlertKey key = new("weekly-movement", "GBP/USD");
            DateTimeOffset timestamp = new(2026, 7, 4, 15, 0, 0, TimeSpan.Zero);
            await CreateStore(path).SetAsync(
                key,
                new AlertDeliveryState(timestamp, "candidate-b"),
                CancellationToken.None);

            JsonAlertStateStore restartedStore = CreateStore(path);
            AlertDeliveryState? reloaded = await restartedStore.GetAsync(key, CancellationToken.None);

            Assert.Equal(timestamp, reloaded?.LastSentUtc);
            Assert.Equal("candidate-b", reloaded?.CandidateFingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLastSentAsync_MalformedJson_ThrowsActionableInvalidDataException()
    {
        string directory = CreateIsolatedDirectory();
        try
        {
            string path = Path.Combine(directory, "alert-state.json");
            await File.WriteAllTextAsync(path, "{ definitely-not-json }");
            JsonAlertStateStore store = CreateStore(path);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetAsync(
                    new AlertKey("daily-movement", "EUR/USD"),
                    CancellationToken.None));

            Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<JsonException>(exception.InnerException);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentWrites_OnOneStore_AreSerializedWithoutLosingKeys()
    {
        string directory = CreateIsolatedDirectory();
        try
        {
            string path = Path.Combine(directory, "alert-state.json");
            JsonAlertStateStore store = CreateStore(path);
            DateTimeOffset baseline = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            AlertKey[] keys = Enumerable.Range(0, 32)
                .Select(index => new AlertKey($"rule-{index:D2}", "EUR/USD"))
                .ToArray();

            Task[] writes = keys.Select((key, index) =>
                store.SetAsync(
                    key,
                    new AlertDeliveryState(baseline.AddMinutes(index), $"candidate-{index}"),
                    CancellationToken.None).AsTask()).ToArray();
            await Task.WhenAll(writes);

            for (int index = 0; index < keys.Length; index++)
            {
                AlertDeliveryState? value = await store.GetAsync(keys[index], CancellationToken.None);
                Assert.Equal(baseline.AddMinutes(index), value?.LastSentUtc);
                Assert.Equal($"candidate-{index}", value?.CandidateFingerprint);
            }

            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(keys.Length, document.RootElement.EnumerateObject().Count());
            Assert.False(File.Exists($"{path}.{Environment.ProcessId}.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RelativePath_IsResolvedFromConfiguredApplicationBasePath()
    {
        string basePath = CreateIsolatedDirectory();
        try
        {
            string relativePath = Path.Combine("runtime", "alert-state.json");
            JsonAlertStateStore store = new(
                Options.Create(new PersistenceOptions { AlertStatePath = relativePath }),
                basePath);

            await store.SetAsync(
                new AlertKey("daily-movement", "EUR/USD"),
                new AlertDeliveryState(
                    new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
                    "candidate-relative"),
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(basePath, relativePath)));
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public void ConfiguredAbsolutePath_IsPreserved()
    {
        string absolutePath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "ForexAlert.Tests",
            Guid.NewGuid().ToString("N"),
            "alert-state.json"));
        JsonAlertStateStore store = new(
            Options.Create(new PersistenceOptions { AlertStatePath = absolutePath }),
            AppContext.BaseDirectory);

        string storedPath = Assert.IsType<string>(typeof(JsonAlertStateStore)
            .GetField("_path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store));

        Assert.Equal(absolutePath, storedPath);
    }

    private static JsonAlertStateStore CreateStore(string path) =>
        new(Options.Create(new PersistenceOptions { AlertStatePath = path }));

    private static string CreateIsolatedDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForexAlert.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
