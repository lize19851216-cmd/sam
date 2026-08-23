# SAM Development Status
## Current phase
Milestone 1 — SteamKit adapter preparation

## Completed
- [x] .NET 10 / WPF
- [x] FakeSteamClient
- [x] Worker Pool
- [x] GitHub Actions
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
- [ ] M1 SteamKit transport implementation (without storing credentials in SAM)

## Verification (2026-08-23)
- `dotnet restore SAM.slnx` — passed
- `dotnet build SAM.slnx --no-restore` — passed, 0 errors
- `dotnet test SAM.slnx --no-build` — passed, 31/31 tests
- CI-equivalent `win-x64` self-contained single-file publish — passed
