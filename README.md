# ForexAlert

ForexAlert is a small, alert-only FX monitor:

```text
IBKR
  -> ForexMonitor
  -> MarketDataState
  -> AlertEvaluator
  -> CooldownStore
  -> LocalFile or Twilio
```

It never places orders and has no portfolio, account-management, prediction, or automated-trading features.

## Architecture

- `Program` loads and validates configuration, selects a notification sender, and wires the application.
- `ForexMonitor` owns the connect, subscribe, refresh, evaluate, and reconnect loop.
- `IbkrMarketDataClient` handles reliable IBKR requests, callbacks, inverse contracts, request IDs, timeouts, and cleanup.
- `MarketDataState` keeps synchronized bid/ask state and completed candle history. `CandleAggregator` creates completed one-minute and hourly candles.
- `AlertEvaluator` contains the daily, hourly, weekly, and optional one-minute calculations and sends eligible alerts.
- `CooldownStore` atomically persists successful sends to JSON. A failed notification never advances cooldown.
- `LocalFileNotificationSender` and `TwilioNotificationSender` are the only notification implementations.

The IBKR client/transport boundary is deliberately retained because it isolates the official external API and allows the connection behavior to be tested offline.

## Build and run

Requires the .NET 10 SDK.

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/ForexAlert/ForexAlert.csproj
```

There are no runtime simulation, dry-run, validation, or run-once command-line modes. Local-file delivery is the safe default and configuration is validated at startup.

## Configuration

Edit `src/ForexAlert/appsettings.json`, use .NET user secrets, or set `FOREXALERT_` environment variables with double underscores for nesting. The application does not load `.env` files; `.env.example` is a reference for values that must be exported or supplied by the launch environment.

```json
"Notification": {
  "Provider": "LocalFile"
}
```

Set `Notification:LocalFilePath` to override the default file under the current user's local application-data directory.

For Twilio:

```json
"Notification": { "Provider": "Twilio" }
```

Supply `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, and at least one `Twilio:Recipients` value outside source control. Automated tests use a fake HTTP handler and never send SMS.

`ForexAlert` contains pairs, thresholds, cooldown/evaluation timing, market timezone, trading-day times, candle capacity, and the optional cooldown-state path. `Ibkr` contains the host, paper/live selection and ports, client ID, and reliability settings. Use the `FOREXALERT_Ibkr__...`, `FOREXALERT_Notification__...`, and `FOREXALERT_Twilio__...` names shown in `.env.example`; never commit actual credentials or phone numbers.

## IBKR API

The public repository does not redistribute `IBApi.dll`. Obtain the official API from Interactive Brokers and pass its local path when building:

```powershell
dotnet build -c Release -p:IBApiAssemblyPath="C:\path\to\IBApi.dll"
```

Without it, the project still builds and tests, but startup reports that live IBKR support is unavailable. Use a paper account, enable the TWS/IB Gateway socket API and Read-Only API mode, and choose a unique client ID.

The client preserves connection timeouts, reconnect handling, historical request pacing, request-ID safety, inverse-contract fallback, quote normalization, completed-history filtering, subscription cleanup, bid/ask synchronization, and stale-quote rejection.

## Market and alert behavior

FX weekend closure is evaluated in `America/New_York` by default with timezone/DST conversion. Daily alerts compare a fresh midpoint with the previous completed daily close (or configured trading-day open). Hourly and optional one-minute alerts use completed candles. Weekly alerts use five distinct completed weekday closes. Threshold comparisons are inclusive and use absolute movement except for the configured sleep-window negative daily threshold.

## Public-repository safety

Do not commit Twilio credentials or numbers, IBKR account data, `IBApi.dll`, alert logs, cooldown files, broker logs, or private fixtures. Relevant runtime paths and secret files are ignored by Git.
