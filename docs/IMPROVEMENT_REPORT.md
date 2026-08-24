# ForexAlert simplification report

## Result

The application now follows one visible path: `Program -> ForexMonitor -> IbkrMarketDataClient -> MarketDataState -> AlertEvaluator -> CooldownStore -> notification sender`.

## Consolidation

- Four alert-rule classes, their base class, and engine/rule interfaces became `AlertEvaluator`.
- Quote state, candle history, and the market-data pipeline became `MarketDataState`; the concrete `CandleAggregator` remains separate because candle completion is stateful and independently testable.
- In-memory/JSON alert-state implementations and their interface became `CooldownStore`.
- Seven option groups and their validator classes became `ForexAlertOptions`, `IbkrOptions`, `NotificationOptions`, `TwilioOptions`, and one direct validation function.
- The production simulation subsystem and its CLI path were removed. Valuable calculation scenarios now live in normal tests.
- Console, dry-run, and null notification providers were removed. LocalFile is the safe default; Twilio is the only external provider.

## Deliberately retained complexity

The IBKR client, transport interface, callback adapter, request registry, and error classifier remain. They implement connection timeout, callback serialization, reconnect signaling, historical pacing/completion, request-ID ownership, inverse-contract fallback, quote-side inversion, subscription cancellation, and proprietary `IBApi.dll` isolation. Flattening those responsibilities would make broker behavior harder to verify and less reliable.

The notification interface also remains because LocalFile and Twilio are genuine external side-effect boundaries and tests must substitute delivery safely.

## Behavior and safety

Daily, hourly, weekly, and optional one-minute alerts remain. Midpoints require valid, fresh, synchronized bid and ask values. Only completed candles are evaluated. Weekend/DST scheduling remains New York-time aware. Cooldowns persist atomically across restarts and are recorded only after a sender succeeds.

The public build still excludes the official IBKR assembly unless `IBApiAssemblyPath` points to an operator-supplied local copy. Secrets, phone numbers, broker data, alert logs, and cooldown files remain outside source control.

## Tests

Tests that existed only for deleted interfaces and implementations were removed. The focused suite covers currency-pair normalization, percentage math, midpoint freshness/skew, completed candles, daily/hourly/weekly/one-minute thresholds, sleep behavior, cooldown persistence and failed delivery, scheduling/DST, LocalFile and Twilio formatting/handling, and detailed IBKR connection/request/inverse/error behavior.
