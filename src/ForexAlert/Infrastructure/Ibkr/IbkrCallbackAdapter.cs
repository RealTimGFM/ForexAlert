using IBApi;

namespace ForexAlert.Infrastructure.Ibkr;

internal sealed class IbkrCallbackAdapter(TimeProvider timeProvider) : DefaultEWrapper
{
    public event Action? Ready;

    public event Action<IbkrTick>? TickReceived;

    public event Action<IbkrHistoricalBar>? HistoricalBarReceived;

    public event Action<int>? HistoricalDataEnded;

    public event Action<IbkrError>? ErrorReceived;

    public event Action? ConnectionClosed;

    public override void nextValidId(int orderId) => Ready?.Invoke();

    public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs) =>
        TickReceived?.Invoke(new IbkrTick(tickerId, field, price, timeProvider.GetUtcNow()));

    public override void historicalData(int reqId, Bar bar) =>
        HistoricalBarReceived?.Invoke(new IbkrHistoricalBar(
            reqId,
            bar.Time,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close));

    public override void historicalDataEnd(int reqId, string start, string end) =>
        HistoricalDataEnded?.Invoke(reqId);

    public override void error(Exception exception) =>
        ErrorReceived?.Invoke(new IbkrError(-1, 0, exception.GetType().Name));

    public override void error(string value) =>
        ErrorReceived?.Invoke(new IbkrError(-1, 0, "IBKR transport error"));

    public override void error(int id, int errorCode, string errorMsg, string advancedOrderRejectJson) =>
        ErrorReceived?.Invoke(new IbkrError(id, errorCode, errorMsg));

    public override void connectionClosed() => ConnectionClosed?.Invoke();
}
