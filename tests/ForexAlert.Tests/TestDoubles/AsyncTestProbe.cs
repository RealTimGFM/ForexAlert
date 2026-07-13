using System.Diagnostics;

namespace ForexAlert.Tests.TestDoubles;

internal static class AsyncTestProbe
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        TimeSpan maximumWait = timeout ?? TimeSpan.FromSeconds(2);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= maximumWait)
            {
                throw new TimeoutException("The asynchronous test condition was not reached in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}
