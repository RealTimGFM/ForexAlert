using System.Text;
using ForexAlert.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Notifications;

public sealed class LocalFileNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<LocalFileNotificationSender> logger) : INotificationSender
{
    private static readonly Encoding Utf8WithoutBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path =
        ResolvePath(options.Value.LocalFilePath);

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task SendAsync(
        AlertCandidate alert,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string line =
            $"{alert.EvaluationTimeUtc:O}\t" +
            $"{AlertMessageFormatter.Format(alert)}" +
            Environment.NewLine;

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(
                    _path,
                    line,
                    Utf8WithoutBom,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        logger.LogInformation(
            "Local alert written to {AlertLogPath} for {Rule} {Symbol}",
            _path,
            alert.RuleName,
            alert.CanonicalSymbol);
    }

    private static string ResolvePath(string configuredPath) =>
        Path.IsPathFullyQualified(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
}
