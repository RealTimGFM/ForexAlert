# Third-party notices

## Interactive Brokers TWS API

ForexAlert does not redistribute Interactive Brokers API source code or binaries. The public source tree and default build contain only user-authored abstractions, a public-safe unavailable transport, and optional adapter code that is compiled only when an operator supplies an existing local `IBApi.dll` through `IBApiAssemblyPath`.

Operators who enable live connectivity must obtain the official TWS API directly from Interactive Brokers and comply with the license that applies to their use. This repository's MIT license does not cover the IBKR API and does not grant any right to copy, publish, or redistribute IBKR source or binaries.

ForexAlert exposes market-data operations only. It does not expose order, portfolio, or managed-account APIs. This notice is informational and is not legal advice.
