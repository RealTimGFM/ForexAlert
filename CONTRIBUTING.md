# Contributing to ForexAlert

Thank you for improving ForexAlert. Keep every contribution offline-testable, secret-free, and inside the alert-only product boundary.

## Ground rules

- Do not add order placement, order staging, portfolio access, managed-account requests, or automated trading.
- Do not commit credentials, account identifiers, recipient numbers, production fixtures, or broker logs.
- Use the dry-run or recording notification sender in every automated test.
- Never commit IBKR-owned source or binaries. Live integration tests must use an external, operator-obtained official `IBApi.dll` through `IBApiAssemblyPath`.
- Use UTC `DateTimeOffset` internally and completed candles in rules.
- Add a regression test for every calculation, concurrency, lifecycle, or parsing change.

## Local checks

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
dotnet run --project src/ForexAlert/ForexAlert.csproj -- --validate-config
dotnet run --project src/ForexAlert/ForexAlert.csproj -- --simulate sample-stream
```

Tests must not require TWS, IB Gateway, Twilio, or a network connection. Use unique temporary directories for persistence tests and deterministic timestamps for schedule/candle tests.

The default build and CI must remain independent of `IBApi.dll`. Any optional live-IBKR check is local-only and must not upload, cache, package, or otherwise redistribute the operator's API files.

## Pull requests

Describe the user-visible behavior, safety impact, configuration changes, tests run, and any live-IBKR behavior that still requires manual verification. Do not paste real broker or notification payloads into an issue or pull request.
