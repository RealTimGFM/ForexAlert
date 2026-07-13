# ForexAlert improvement report

## 1. Original problems found

The baseline was an old-style .NET Framework 4.7.2 executable named ForexAI. It used legacy project metadata and package resolution, duplicated market-data and scheduling paths, exposed hardcoded notification configuration, and used AI/trade terminology for a deterministic heuristic. Runtime review also found inert alert paths, unsafe historical-notification coupling, inconsistent symbol handling, incomplete candle semantics, overlapping timers, non-persisted cooldowns, and Windows-specific scheduling assumptions.

No user-authored path placed or cancelled an order. A dormant managed-account request and the vendor's broad API surface were removed from the public application boundary.

## 2. Architectural decisions

- Renamed the solution, project, assembly, and user-owned namespaces to `ForexAlert`.
- Targeted `net10.0` with nullable references, deterministic output, warnings as errors, Generic Host, dependency injection, validated options, cancellation, and `TimeProvider`.
- Kept user-authored IBKR-facing interfaces separate from the alert engine and market-data pipeline.
- Removed redistributed IBKR source and binaries. The default build uses an actionable unavailable transport and stays offline.
- Made live support optional: an operator can supply an official, separately obtained `IBApi.dll` with `IBApiAssemblyPath`; only then are the user-authored adapter and socket transport compiled.
- Enforced connection timeouts in ForexAlert with tasks, cancellation, and `WaitAsync`, without patching or assuming a custom vendor `ConnectTimeout` property.
- Retained one market-data connection, one request-ID registry, one pair catalog, completed-candle rules, bounded stores, and atomic cooldown persistence.
- Implemented Twilio with `HttpClient` and added Console, DryRun, LocalFile, Null, and Twilio notification providers. LocalFile writes only to local storage and is not external delivery.
- Removed the disconnected momentum feature and preserved the alert-only boundary.

## 3. Code and repository organization

The modern application lives in `src/ForexAlert`, offline tests live in `tests/ForexAlert.Tests`, and CI lives in `.github/workflows/ci.yml`. The former `ForexAlert.ThirdParty.IBKR` project and `src/ForexAlert/ThirdParty/IBKR` tree were removed. Test-wide xUnit and configuration imports now live at `tests/ForexAlert.Tests/GlobalUsings.cs`.

The legacy `ForexAI.sln`, project metadata, static fetchers, overlapping scanners and schedulers, direct Twilio helper, prediction/trade-signal folder, and ad hoc output paths are removed in the working-tree modernization.

## 4. Security and licensing

- Removed hardcoded Twilio authentication, sender, and recipient values from application code.
- Safe configuration defaults to DryRun; live Twilio requires explicit selection, `DryRun=false`, and validated external secrets.
- Local alert logs and cooldown state default below the user's local application-data directory and are ignored by Git.
- The repository no longer redistributes IBKR source or binaries and grants no IBKR redistribution rights. Operators must obtain the official API directly from Interactive Brokers and comply with its license.
- Current-tree cleanup does not erase prior Git history. Historical credentials require rotation, and historical IBKR files require a deliberate history-remediation decision before public release.

## 5. Alert-calculation corrections

- Daily movement uses a previous completed close by default, with an explicit trading-day-open alternative.
- Hourly and one-minute rules use completed candles; weekly movement uses distinct completed trading-day closes.
- Quotes are validated for freshness, bid/ask skew, ordering, and inverse normalization.
- Cooldown state advances only after successful delivery and persists atomically.
- Alerts retain canonical symbol, rule, baseline/current values and timestamps, threshold, direction, and UTC/local evaluation context.

## 6. Test coverage

The final offline xUnit suite contains 193 tests, including the 192-test modernized baseline plus an actionable-error test for the public-safe unavailable IBKR transport. Coverage includes parsing and calculations, quote/candle correctness, alert rules, persistence, scheduling and DST, configuration validation, all notification safety paths including LocalFile, IBKR client timeout/retry behavior through test doubles, graceful shutdown, and offline simulation.

## 7. Verification policy

Release verification runs restore, format, Release build, all offline tests, formatting verification, safe configuration validation, offline simulation, whitespace checks, and both history and current-tree secret scans. Stale command timings were removed because they are environment-dependent; current command results belong in the release handoff.

## 8. Remaining live-environment limitations

- Validate broker entitlements, contract availability, timestamp formats, pacing, farm-status changes, reconnect behavior, and shutdown against the operator's current TWS or IB Gateway.
- Confirm the selected client ID and paper/live port locally.
- Validate optional Twilio delivery manually only after dry-run acceptance; automated tests never send SMS.

## 9. Public-safe IBKR integration

The public build has no IBKR-owned source or binary and cannot connect live. `IIbkrTransport`, the market-data client, simulation, alert engine, persistence, and offline tests remain independently buildable. When `IBApiAssemblyPath` names an existing official local DLL, MSBuild references that external assembly and includes only ForexAlert's user-authored adapter and transport. No order, portfolio, managed-account, or trading API is exposed.

## 10. Deferred enhancements

- A maintained FX holiday and exceptional-closure calendar.
- Database persistence, distributed locks, dashboards, metrics exporters, or cloud deployment manifests.
- Dynamic pair reconfiguration without restart.
- Additional external notification providers beyond Twilio.
- Machine-learning forecasts or any automatic trading feature.
- Automatic secrets-vault integration beyond standard .NET user-secrets and environment providers.
