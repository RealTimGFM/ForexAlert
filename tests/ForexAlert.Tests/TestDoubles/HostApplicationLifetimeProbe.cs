using Microsoft.Extensions.Hosting;

namespace ForexAlert.Tests.TestDoubles;

internal sealed class HostApplicationLifetimeProbe : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    private int _stopApplicationCount;

    public CancellationToken ApplicationStarted => _started.Token;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => _stopped.Token;

    public int StopApplicationCount => Volatile.Read(ref _stopApplicationCount);

    public void StopApplication()
    {
        Interlocked.Increment(ref _stopApplicationCount);
        _stopping.Cancel();
    }

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }
}
