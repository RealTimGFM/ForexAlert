# Changelog

All notable changes to ForexAlert are documented here. The format follows Keep a Changelog, and the project intends to use semantic versioning once releases begin.

## [Unreleased]

### Added

- .NET 10 Generic Host application with validated configuration and graceful shutdown.
- Unified daily, hourly, weekly, and optional one-minute alert rules.
- Canonical currency-pair, quote, completed-candle, and bounded history models.
- Atomic JSON cooldown persistence.
- LocalFile and Twilio notification providers.
- Focused offline xUnit test project.
- Cross-platform New York FX weekend schedule with daylight-saving coverage.
- GitHub Actions CI, Dependabot, security, contribution, licensing, and third-party documentation.

### Changed

- Simplified the runtime to `Program`, `ForexMonitor`, `MarketDataState`, `CandleAggregator`, `AlertEvaluator`, `CooldownStore`, and the IBKR/notification boundaries.
- Consolidated alert rules, market-data stores, cooldown persistence, configuration, and startup validation into fewer concrete concepts.
- Reduced notifications to LocalFile and Twilio, with LocalFile as the safe default.
- Replaced abstraction-oriented and simulation tests with a focused behavioral suite.
- Renamed solution, project, assembly, and user-owned namespaces from ForexAI to ForexAlert.
- Made live IBKR support optional through an operator-supplied official `CSharpAPI.dll`, with connection timeout enforcement in user-authored code.
- Replaced static dictionaries, overlapping timers, direct file writes, and hardcoded values with injected services and options.

### Removed

- Runtime simulation and `--simulate`, `--dry-run`, `--once`, and `--validate-config` command-line modes.
- Rule/engine, market-data-store, cooldown-store, and schedule interfaces that had only one application implementation.
- Console, dry-run, null, and in-memory notification/state implementations.
- Redistributed IBKR source and binaries from the public tree and default build.
- Hardcoded Twilio values and recipient identifiers.
- Duplicate scanners/schedulers, legacy package files, non-SDK project metadata, and ad hoc text/CSV logging.
- The disconnected deterministic momentum feature and misleading AI/trade terminology.
