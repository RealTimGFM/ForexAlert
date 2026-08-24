using System.Net.Http.Headers;
using System.Text;
using ForexAlert.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForexAlert.Notifications;

public sealed class TwilioNotificationSender(
    HttpClient httpClient,
    IOptions<TwilioOptions> twilioOptions,
    TimeProvider timeProvider,
    ILogger<TwilioNotificationSender> logger) : INotificationSender
{
    private readonly TwilioOptions _options = twilioOptions.Value;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, DeliveryCacheEntry> _successfulRecipients = new(StringComparer.Ordinal);

    public async Task SendAsync(AlertCandidate alert, CancellationToken cancellationToken)
    {
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        string requestPath = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        string message = AlertMessageFormatter.Format(alert);
        string deliveryKey = $"{alert.RuleName}|{alert.CanonicalSymbol}|{alert.DeliveryFingerprint}";
        DeliveryCacheEntry entry = AcquireEntry(deliveryKey);
        bool entered = false;
        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            await SendRecipientsAsync(alert, requestPath, credentials, message, entry, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (entered)
            {
                entry.Gate.Release();
            }

            ReleaseEntry(entry);
        }
    }

    private async Task SendRecipientsAsync(
        AlertCandidate alert,
        string requestPath,
        string credentials,
        string message,
        DeliveryCacheEntry entry,
        CancellationToken cancellationToken)
    {
        List<Exception> failures = [];

        foreach (string recipient in _options.Recipients)
        {
            if (entry.Delivered.Contains(recipient))
            {
                continue;
            }

            using HttpRequestMessage request = new(HttpMethod.Post, requestPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Body", message),
                new KeyValuePair<string, string>("From", _options.FromNumber),
                new KeyValuePair<string, string>("To", recipient),
            ]);

            try
            {
                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Twilio returned HTTP {(int)response.StatusCode} ({response.StatusCode}) for one configured recipient.",
                        inner: null,
                        response.StatusCode);
                }

                entry.Delivered.Add(recipient);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                logger.LogError(
                    exception,
                    "Twilio delivery failed for one recipient of {Rule} {Symbol}; successful recipients will not be retried",
                    alert.RuleName,
                    alert.CanonicalSymbol);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Twilio delivery failed for {failures.Count} of {_options.Recipients.Count} configured recipient(s).",
                failures);
        }

        logger.LogInformation(
            "Twilio delivered {Rule} alert for {Symbol} to {RecipientCount} configured recipient(s)",
            alert.RuleName,
            alert.CanonicalSymbol,
            entry.Delivered.Count);
    }

    private DeliveryCacheEntry AcquireEntry(string deliveryKey)
    {
        lock (_cacheGate)
        {
            DateTimeOffset nowUtc = timeProvider.GetUtcNow();
            RemoveExpiredEntries(nowUtc);
            if (!_successfulRecipients.TryGetValue(deliveryKey, out DeliveryCacheEntry? entry))
            {
                if (_successfulRecipients.Count >= _options.SuccessfulRecipientCacheCapacity)
                {
                    KeyValuePair<string, DeliveryCacheEntry>? oldestIdle = _successfulRecipients
                        .Where(pair => pair.Value.Users == 0 &&
                            pair.Value.Delivered.Count >= _options.Recipients.Count)
                        .OrderBy(static pair => pair.Value.LastAccessUtc)
                        .Cast<KeyValuePair<string, DeliveryCacheEntry>?>()
                        .FirstOrDefault();
                    if (!oldestIdle.HasValue)
                    {
                        throw new InvalidOperationException(
                            "The bounded Twilio successful-recipient cache is fully occupied by active or partial deliveries.");
                    }

                    _successfulRecipients.Remove(oldestIdle.Value.Key);
                }

                entry = new DeliveryCacheEntry(nowUtc);
                _successfulRecipients.Add(deliveryKey, entry);
            }

            entry.Users++;
            entry.LastAccessUtc = nowUtc;
            return entry;
        }
    }

    private void ReleaseEntry(DeliveryCacheEntry entry)
    {
        lock (_cacheGate)
        {
            entry.Users--;
            entry.LastAccessUtc = timeProvider.GetUtcNow();
            RemoveExpiredEntries(entry.LastAccessUtc);
        }
    }

    private void RemoveExpiredEntries(DateTimeOffset nowUtc)
    {
        string[] expiredKeys = _successfulRecipients
            .Where(pair => pair.Value.Users == 0 &&
                nowUtc - pair.Value.LastAccessUtc >= _options.SuccessfulRecipientCacheDuration)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (string key in expiredKeys)
        {
            _successfulRecipients.Remove(key);
        }
    }

    private sealed class DeliveryCacheEntry(DateTimeOffset lastAccessUtc)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public HashSet<string> Delivered { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset LastAccessUtc { get; set; } = lastAccessUtc;

        public int Users { get; set; }
    }
}
