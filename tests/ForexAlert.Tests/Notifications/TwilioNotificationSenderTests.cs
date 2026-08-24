using System.Net;
using ForexAlert.Domain;
using ForexAlert.Notifications;
using ForexAlert.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ForexAlert.Tests.Notifications;

public sealed class TwilioNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_PartialFailure_RetriesOnlyRecipientsNotAlreadyDelivered()
    {
        SequencedResponseHandler handler = new(
            HttpStatusCode.Created,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.Created);
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.twilio.invalid/", UriKind.Absolute),
        };
        TwilioNotificationSender sender = new(
            httpClient,
            Options.Create(new TwilioOptions
            {
                AccountSid = "AC_test_account",
                AuthToken = "test-token-not-a-secret",
                FromNumber = "+15555550101",
                Recipients = ["+15555550102", "+15555550103"],
                SuccessfulRecipientCacheDuration = TimeSpan.FromHours(24),
                SuccessfulRecipientCacheCapacity = 10_000,
            }),
            TimeProvider.System,
            NullLogger<TwilioNotificationSender>.Instance);
        AlertCandidate alert = CreateAlert();

        await Assert.ThrowsAsync<AggregateException>(() =>
            sender.SendAsync(alert, CancellationToken.None));
        await sender.SendAsync(alert, CancellationToken.None);
        await sender.SendAsync(alert, CancellationToken.None);

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_ExpiredPartialSuccess_ForgetsRecipientsAndRemainsBounded()
    {
        SequencedResponseHandler handler = new(
            HttpStatusCode.Created,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.Created,
            HttpStatusCode.Created);
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.twilio.invalid/", UriKind.Absolute),
        };
        AdjustableTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        TwilioNotificationSender sender = CreateLiveSender(
            httpClient,
            timeProvider,
            cacheDuration: TimeSpan.FromMinutes(1));
        AlertCandidate alert = CreateAlert();

        await Assert.ThrowsAsync<AggregateException>(() => sender.SendAsync(alert, CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await sender.SendAsync(alert, CancellationToken.None);

        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_ConcurrentSameAlert_DeliversEachRecipientOnlyOnce()
    {
        ProbeHttpMessageHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.twilio.invalid/", UriKind.Absolute),
        };
        TwilioNotificationSender sender = CreateLiveSender(httpClient, TimeProvider.System);
        AlertCandidate alert = CreateAlert();

        await Task.WhenAll(
            sender.SendAsync(alert, CancellationToken.None),
            sender.SendAsync(alert, CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
    }

    private static TwilioNotificationSender CreateLiveSender(
        HttpClient httpClient,
        TimeProvider timeProvider,
        TimeSpan? cacheDuration = null) =>
        new(
            httpClient,
            Options.Create(new TwilioOptions
            {
                AccountSid = "AC_test_account",
                AuthToken = "test-token-not-a-secret",
                FromNumber = "+15555550101",
                Recipients = ["+15555550102", "+15555550103"],
                SuccessfulRecipientCacheDuration = cacheDuration ?? TimeSpan.FromHours(1),
                SuccessfulRecipientCacheCapacity = 10,
            }),
            timeProvider,
            NullLogger<TwilioNotificationSender>.Instance);

    private static AlertCandidate CreateAlert()
    {
        DateTimeOffset baseline = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset current = baseline.AddHours(1);
        return new AlertCandidate(
            "hourly-movement",
            CurrencyPair.Parse("EUR/USD"),
            baseline,
            1.00,
            current,
            1.02,
            2.0,
            AlertDirection.Up,
            1.4,
            current,
            "2026-07-01 08:00:00 -04:00 (America/New_York)");
    }

    private sealed class SequencedResponseHandler(params HttpStatusCode[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses = new(responses);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            HttpStatusCode status = _responses.Count > 0
                ? _responses.Dequeue()
                : HttpStatusCode.Created;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
