# Security policy

Report vulnerabilities privately through GitHub security advisories when available. Never include credentials, phone numbers, account identifiers, broker data, or private market logs in public issues.

- Use LocalFile while developing and a paper IBKR account with Read-Only API enabled.
- Store Twilio settings in user secrets, environment variables, or another private configuration source.
- Rotate any credential that has appeared in Git history.
- Protect the cooldown file from untrusted writes because it controls alert suppression.
- Do not commit `CSharpAPI.dll`, alert logs, cooldown files, broker logs, or private fixtures.

ForexAlert does not log Twilio credentials or full recipient numbers and exposes no order or account-management API.
