# Security policy

## Reporting a vulnerability

Use the repository's private GitHub security-advisory feature when available. Do not open a public issue containing credentials, phone numbers, broker identifiers, account data, or exploit details. If private reporting is unavailable, contact the repository owner through a previously established private channel.

## Operational guidance

- Keep `Notifications:DryRun=true` until an external provider has been deliberately reviewed.
- Store Twilio values in user secrets, environment variables, or a managed secret store.
- Rotate any credential that has ever appeared in source history; deleting it from the current tree does not remove it from old commits.
- Use an IBKR paper account and enable **Read-Only API**.
- Give ForexAlert a unique IBKR client ID and only the filesystem permissions needed for its state directory.
- Treat simulation and broker fixtures as public data; remove account fields before adding them.
- Protect the configured alert-state file from untrusted writes because it controls alert suppression. By default, it is stored under the user's local application-data directory in `ForexAlert/alert-state.json`.
- Do not commit local alert logs, cooldown state, `alerts.txt`, `alert-state.json`, or other runtime data; review them for market or operational metadata before sharing.

ForexAlert logs structured market and rule metadata but intentionally does not log Twilio credentials, full recipients, or managed-account identifiers.

## Supported versions

Security fixes are applied to the current `main` branch. No older release line is currently maintained.
