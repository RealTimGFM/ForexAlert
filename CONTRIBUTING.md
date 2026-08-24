# Contributing to ForexAlert

Keep changes inside the alert-only boundary: no orders, portfolio access, account management, predictions, or automated trading. Do not commit credentials, phone numbers, broker data, `CSharpAPI.dll`, alert logs, or cooldown files.

Use UTC `DateTimeOffset` internally, completed candles for interval alerts, and fake external boundaries in automated tests. Preserve the IBKR reliability safeguards unless a change is backed by a regression test.

Run:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
git diff --check
```

The default build and tests must not require TWS, IB Gateway, Twilio, or `CSharpAPI.dll`.
