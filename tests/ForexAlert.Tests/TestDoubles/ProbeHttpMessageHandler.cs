using System.Net;

namespace ForexAlert.Tests.TestDoubles;

internal sealed class ProbeHttpMessageHandler : HttpMessageHandler
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
