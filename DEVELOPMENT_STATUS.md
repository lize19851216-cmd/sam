# SAM Development Status
## Current phase
Milestone 1 — safe SteamKit transport foundation

## Completed
- [x] .NET 10 / WPF
- [x] FakeSteamClient
- [x] Worker Pool
- [x] GitHub Actions
- [x] GitHub Actions uses least-privilege repository access and cancels superseded branch builds
- [x] Local and CI Windows release builds publish a SHA-256 checksum manifest
- [x] GitHub Actions restores Windows runtime assets before self-contained publish
- [x] Microsoft.Data.Sqlite upgraded to 10.0.11 to remove the reported SQLite native-library vulnerability
- [x] SQLite task persistence (`Tasks` table, concurrent-safe connections)
- [x] Structured Serilog file logging (application enrichment and JSON properties)
- [x] Task state transition logging with structured task and account properties
- [x] Task Center execution state machine with retry, timeout and cancellation
- [x] Plugin contract, load report and duplicate-ID protection
- [x] Unit tests for task lifecycle, SQLite task storage and plugin registry

## Next
- [x] Persist GUI accounts to SQLite and show persisted task history
- [x] Atomic replacement of regenerated simulated-account snapshots
- [x] Task Center UI (status, retry count, cancellation and task history)
- [x] Task Center real-time UI updates for running, retrying and terminal states
- [x] SQLite Task Center history pagination API
- [x] SQLite WAL and busy-timeout configuration with concurrent task-write coverage
- [x] SQLite Task Center terminal-history retention API that protects active tasks
- [x] Task Center UI offers confirmed cleanup of terminal history older than 90 days
- [x] Task Center UI “load more” history control
- [x] Task Center stable cursor pagination during live task updates
- [x] Task Center observer fault isolation
- [x] Cancellation persistence for queued as well as active login tasks
- [x] Plugin management UI (local plugin discovery and load report)
- [x] Plugin lifecycle policy (reverse-order shutdown and disposable resource release)
- [x] Default-deny trusted-plugin SHA-256 manifest policy
- [x] Desktop UI hash copy flow for plugin trust review
- [x] Plugin failure diagnostics tolerate missing or unreadable assemblies
- [x] Enforced default-deny execution policy for untrusted third-party plugins
- [x] Restricted metadata-only IPC contract for a future isolated plugin host
- [x] Local named-pipe metadata transport for the isolated plugin host contract
- [x] Current-user-only named-pipe endpoint validation for plugin metadata transport
- [x] 10 simulated account concurrency and cancellation reliability tests
- [x] Worker Pool hard concurrency cap of 10 (including UI input normalization)
- [x] Worker Pool isolates account-update observer failures from background task execution
- [x] Worker Pool isolates per-account task persistence failures without aborting a batch
- [x] Desktop login batches use an exclusive operation gate to prevent overlapping runs
- [x] Account generation shares the desktop operation gate and refreshes the UI only after persistence succeeds
- [x] M1 SteamKit adapter boundary (sanitized transport contract and mapping adapter)
- [x] M1 safe client selection and explicit opt-in configuration
- [x] Desktop startup uses the safe client factory and remains in Fake mode by default
- [x] M1 SteamKit transport implementation with short-lived sessions and external, non-persisting credential configuration

## Next phase
- [x] M2: sanitize SteamKit transport failures while preserving caller-requested cancellation
- [x] M2: define a current-user-only local authentication broker protocol that exchanges account names and sanitized outcomes only
- [x] M2: implement a separately launched, one-request console broker that owns interactive credentials in memory only
- [x] M2: CI and local release build publish the separately launched authentication broker as its own Windows artifact
- [x] M2: desktop offers an explicit, confirmed external-broker selection while defaulting to FakeSteamClient on every start
- [x] M2: automated no-credential end-to-end verification covers broker, Steam adapter, Worker Pool, and Task Center states
- [x] M2: desktop can probe the local external broker without sending an account or requesting credentials
- [x] M2: the standalone broker remains available for subsequent local requests, and desktop blocks mock simulated-account batches from reaching it
- [x] M2: continuous broker service is covered by repeated no-credential probe tests
- [x] M2: standalone broker integration is validated through repeated local probes with no account or credential data
- [x] M2: standalone broker smoke tests execute the broker matching the active Debug or Release test configuration
- [x] M2: SQLite task history normalizes timestamps to UTC so cross-time-zone records paginate in chronological order
- [x] M2: plugin metadata IPC rejects oversized outbound messages before they reach a local client
- [x] M2: plugin metadata IPC uses a bounded client operation timeout when no isolated host is available
- [x] M2: plugin metadata IPC validates complete result contracts before sending or accepting metadata
- [x] M2: account SQLite storage uses WAL, a bounded busy timeout, cancellation-aware writes, and concurrent-write coverage

## Verification (2026-08-23)
- `dotnet restore SAM.slnx` — passed
- `dotnet build SAM.slnx --no-restore` — passed, 0 errors (current environment reports `NU1900` because NuGet vulnerability-index access is unavailable)
- `dotnet test SAM.slnx --no-build` — passed, 59/59 tests
- `dotnet test tests/SAM.Core.Tests/SAM.Core.Tests.csproj -c Release --no-build` — passed, 59/59 tests
- CI-equivalent `win-x64` self-contained single-file publish for desktop and authentication broker — passed
