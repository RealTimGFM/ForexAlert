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
Copy-Item .env.example .env
# Edit .env for your IBKR connection, alert rules, paths, and notification provider.
dotnet restore
dotnet build -c Release -p:IBApiAssemblyPath="$env:IBApiAssemblyPath"
dotnet test -c Release --no-build
dotnet run --project src/ForexAlert/ForexAlert.csproj
```

There are no runtime simulation, dry-run, validation, or run-once command-line modes. Configuration is validated at startup.

## Configuration

For local development, copy `.env.example` to `.env` and edit `.env`. It contains every supported runtime setting and is the local source of truth. `appsettings.json` contains framework logging only.

The application loads the nearest `.env` before building the host. Existing OS or container variables take precedence, so deployment systems can override a local file. Runtime keys use the `FOREXALERT_` prefix and double underscores for nesting, for example `FOREXALERT_Notification__Provider=LocalFile`; collections use indexed keys such as `FOREXALERT_ForexAlert__CurrencyPairs__0=EUR/USD`.

Set `FOREXALERT_Notification__LocalFilePath` for local-file delivery.

For Twilio:

```dotenv
FOREXALERT_Notification__Provider=Twilio
```

Supply `FOREXALERT_Twilio__AccountSid`, `FOREXALERT_Twilio__AuthToken`, `FOREXALERT_Twilio__FromNumber`, and at least one indexed `FOREXALERT_Twilio__Recipients__0` value outside source control. Automated tests use a fake HTTP handler and never send SMS.

`ForexAlert` contains pairs, thresholds, cooldown/evaluation timing, market timezone, trading-day times, candle capacity, and the cooldown-state path. `Ibkr` contains the host, paper/live selection and ports, client ID, and reliability settings. Use the names shown in `.env.example`; never commit actual credentials or phone numbers.

## IBKR API

The public repository does not redistribute `CSharpAPI.dll`. Obtain the official API from Interactive Brokers and pass its local path when building:

```powershell
dotnet build -c Release -p:IBApiAssemblyPath="C:\path\to\CSharpAPI.dll"
```

Without it, the project still builds and tests, but startup reports that live IBKR support is unavailable. Use a paper account, enable the TWS/IB Gateway socket API and Read-Only API mode, and choose a unique client ID.

The client preserves connection timeouts, reconnect handling, historical request pacing, request-ID safety, inverse-contract fallback, quote normalization, completed-history filtering, subscription cleanup, bid/ask synchronization, and stale-quote rejection.

## Market and alert behavior

FX weekend closure is evaluated in the configured market timezone with timezone/DST conversion. Daily alerts compare a fresh midpoint with the configured previous-close or trading-day-open baseline. Hourly and optional one-minute alerts use completed candles. Weekly alerts use five distinct completed weekday closes. Threshold comparisons are inclusive and use absolute movement except for the configured sleep-window negative daily threshold.

## Public-repository safety

Do not commit Twilio credentials or numbers, IBKR account data, `CSharpAPI.dll`, alert logs, cooldown files, broker logs, or private fixtures. Relevant runtime paths and secret files are ignored by Git.
