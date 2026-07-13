# Changelog

All notable changes to ForexAlert are documented here. The format follows Keep a Changelog, and the project intends to use semantic versioning once releases begin.

## [Unreleased]

### Added

- .NET 10 Generic Host application with validated configuration and graceful shutdown.
- Unified daily, hourly, weekly, and optional one-minute alert rules.
- Canonical currency-pair, quote, completed-candle, and bounded history models.
- Atomic JSON cooldown persistence.
- Console, dry-run, LocalFile, null, and Twilio notification providers.
- Offline end-to-end simulation and xUnit test project.
- Cross-platform New York FX weekend schedule with daylight-saving coverage.
- GitHub Actions CI, Dependabot, security, contribution, licensing, and third-party documentation.

### Changed

- Renamed solution, project, assembly, and user-owned namespaces from ForexAI to ForexAlert.
- Made live IBKR support optional through an operator-supplied official `IBApi.dll`, with connection timeout enforcement in user-authored code.
- Replaced static dictionaries, overlapping timers, direct file writes, and hardcoded values with injected services and options.

### Removed

- Redistributed IBKR source and binaries from the public tree and default build.
- Hardcoded Twilio values and recipient identifiers.
- Duplicate scanners/schedulers, legacy package files, non-SDK project metadata, and ad hoc text/CSV logging.
- The disconnected deterministic momentum feature and misleading AI/trade terminology.
