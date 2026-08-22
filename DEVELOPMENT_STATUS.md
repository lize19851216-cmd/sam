# SAM Development Status
## Current phase
Milestone 1 — SteamKit adapter preparation

## Completed
- [x] .NET 10 / WPF
- [x] FakeSteamClient
- [x] Worker Pool
- [x] GitHub Actions
- [x] SQLite task persistence (`Tasks` table, concurrent-safe connections)
- [x] Structured Serilog file logging (application enrichment and JSON properties)
- [x] Task Center execution state machine with retry, timeout and cancellation
- [x] Plugin contract, load report and duplicate-ID protection
- [x] Unit tests for task lifecycle, SQLite task storage and plugin registry

## Next
- [x] Persist GUI accounts to SQLite and show persisted task history
- [x] Task Center UI (status, retry count, cancellation and task history)
- [x] Plugin management UI (local plugin discovery and load report)
- [ ] Plugin unload/isolation policy
- [x] 10 simulated account concurrency and cancellation reliability tests
- [x] M1 SteamKit adapter boundary (sanitized transport contract and mapping adapter)
- [x] M1 safe client selection and explicit opt-in configuration
- [x] Desktop startup uses the safe client factory and remains in Fake mode by default
- [ ] M1 SteamKit transport implementation (without storing credentials in SAM)

## Verification (2026-08-22)
- `dotnet restore SAM.slnx` — passed (NuGet audit index unavailable locally: NU1900 warning only)
- `dotnet build SAM.slnx --no-restore` — passed, 0 errors
- `dotnet test SAM.slnx --no-build` — passed, 11/11 tests
