using ForexAlert.Infrastructure.Ibkr;

namespace ForexAlert.Tests.Infrastructure;

public sealed class UnavailableIbkrTransportTests
{
    [Fact]
    public async Task ConnectAsync_ExplainsHowToEnableOfficialLocalApi()
    {
        await using UnavailableIbkrTransport transport = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.ConnectAsync(
                "127.0.0.1",
                4002,
                0,
                TimeSpan.FromSeconds(10),
                CancellationToken.None));

        Assert.Contains("official TWS API", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBApiAssemblyPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBApi.dll", exception.Message, StringComparison.Ordinal);
    }
}
