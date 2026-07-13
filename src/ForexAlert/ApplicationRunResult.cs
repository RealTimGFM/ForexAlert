namespace ForexAlert;

public sealed class ApplicationRunResult
{
    public const int NoFreshQuoteExitCode = 3;

    private int _exitCode;

    public int ExitCode => Volatile.Read(ref _exitCode);

    public void SetNoFreshQuote() => Interlocked.CompareExchange(ref _exitCode, NoFreshQuoteExitCode, 0);
}
