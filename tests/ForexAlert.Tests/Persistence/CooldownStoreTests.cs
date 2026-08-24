using ForexAlert.Domain;
using ForexAlert.Persistence;

namespace ForexAlert.Tests.Persistence;

public sealed class CooldownStoreTests
{
    [Fact]
    public async Task MarkSent_SuppressesDuplicateAndCooldown_AndPersistsAcrossRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ForexAlert-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "cooldowns.json");
        DateTimeOffset now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        AlertCandidate first = Alert(now, 1.02);
        AlertCandidate changed = Alert(now.AddMinutes(1), 1.03);
        try
        {
            CooldownStore store = new(path, directory);
            Assert.True(await store.CanSendAsync(first, now, TimeSpan.FromHours(1), CancellationToken.None));
            await store.MarkSentAsync(first, now, CancellationToken.None);
            Assert.False(await store.CanSendAsync(first, now.AddHours(2), TimeSpan.FromHours(1), CancellationToken.None));
            Assert.False(await store.CanSendAsync(changed, now.AddMinutes(30), TimeSpan.FromHours(1), CancellationToken.None));

            CooldownStore restarted = new(path, directory);
            Assert.True(await restarted.CanSendAsync(changed, now.AddHours(1), TimeSpan.FromHours(1), CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static AlertCandidate Alert(DateTimeOffset time, double current) => new(
        "hourly-movement", CurrencyPair.Parse("EUR/USD"), time.AddHours(-1), 1.00, time, current,
        (current - 1) * 100, AlertDirection.Up, 1.0, time, "market time");
}
