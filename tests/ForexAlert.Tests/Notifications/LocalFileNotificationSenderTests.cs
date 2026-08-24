using ForexAlert.Domain;
using ForexAlert.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Notifications;

public sealed class LocalFileNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_AppendsOneLinePerAlert()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"ForexAlert-{Guid.NewGuid():N}");

        string path = Path.Combine(directory, "alerts.txt");

        try
        {
            LocalFileNotificationSender sender = new(
                Options.Create(new NotificationOptions
                {
                    LocalFilePath = path,
                }),
                NullLogger<LocalFileNotificationSender>.Instance);

            AlertCandidate alert = CreateAlert();

            await sender.SendAsync(alert, CancellationToken.None);
            await sender.SendAsync(alert, CancellationToken.None);

            string[] lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(2, lines.Length);

            Assert.All(lines, line =>
            {
                Assert.Contains("hourly-movement", line);
                Assert.Contains("EUR/USD", line);
                Assert.Contains("+2.0000%", line);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AlertCandidate CreateAlert()
    {
        DateTimeOffset baseline =
            new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        DateTimeOffset current = baseline.AddHours(1);

        return new AlertCandidate(
            "hourly-movement",
            CurrencyPair.Parse("EUR/USD"),
            baseline,
            1.00d,
            current,
            1.02d,
            2.00d,
            AlertDirection.Up,
            1.40d,
            current,
            "2026-07-01 08:00:00 -04:00 (America/New_York)");
    }
}
