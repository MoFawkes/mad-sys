# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Added (Phase 5 — staff UI)

- Generic Host WPF composition root with single-instance activation, application lifetime wiring, structured global exception handling, and window navigation behind `IWindowService`.
- One-second dispatcher clock publishing `ClockTick` and resume/clock-change `TimeJumped` messages.
- Invite-only sign-in and password-reset UI, initial-sync progress, authenticated startup routing, and shared-machine sign-out routing.
- Normal and compact clock layouts using the Domain schedule engine over SQLite-backed repositories, including current/next lesson, countdown, progress, day list, connectivity status, admin placeholder, pinning, and per-mode placement.
- Read-only announcements panel with poster names, relative times, unread counts, expiry filtering, and local read state.
- Typed JSON settings, light/dark/system themes, notification preferences, account controls, and log-folder access.
- ViewModel and settings tests plus a live Supabase password-recovery smoke test; local totals are 74 non-Supabase and 175 live Supabase tests.
- Verified the Phase 5 automated gate in CI: Windows build/ViewModel tests and Ubuntu clean resets plus all 175 Supabase tests passed.

### Fixed (Phase 5 — J1 acceptance)

- Prevented initial sign-in command evaluation from throwing on empty or whitespace email input.
- Initialized the disposable SQLite cache before session restore/sign-in can query a cached profile, allowing the role to arrive later with the Profiles snapshot on a true first run.
- Kept unexpected local persistence failures inside the sign-in error state instead of escalating them to the global crash dialog.
- Added seam-level regressions for blank-email command gating, unexpected persistence errors, and real fresh-file cache initialization during sign-in.
- Applied compact mode as an explicit fixed 320×80 frameless window presentation, avoiding WPF local-value precedence over style triggers.
- Stopped sign-in routing from constructing a hidden main window; closing sign-in while signed out now exits cleanly, and signing out closes Settings before returning to sign-in.
- Serialized atomic settings writes and debounced placement persistence so rapid resize/drag/mode events cannot race over `settings.json.tmp`.
- Suppressed intermediate geometry during mode transitions, normalized compact placement to 320×80, and moved compact dragging to mouse-move so double-click restore is reliable.

### Added (Phase 4 — client infrastructure)

- Application-layer cache, repository, Supabase, session, sync, and local-state contracts plus messenger events and pure debounce/backoff policies.
- Disposable WAL-mode SQLite cache with embedded ordered migrations, integrity/migration recovery, atomic per-table snapshot replacement, typed repositories, notification dedup pruning, and announcement read state.
- DPAPI CurrentUser session persistence and shared-machine sign-out cleanup.
- Pinned Supabase C# gateway for password auth, token refresh, sign-out, all-table snapshot pulls, admin writes, Realtime change signals, and one-time clock-skew warnings.
- Session and sync orchestration with offline cache-display behavior, organisation-change wipes, 500 ms Realtime debounce, 30-second heartbeat, five-minute capped backoff, and immediate network-change retry.
- Unit, SQLite, DPAPI, and live gateway smoke coverage; the local Supabase suite now contains 174 passing tests and is repeatable without a database reset.
- Updated the SQLite provider to the current .NET 8 servicing release after dependency audit identified a vulnerable older native SQLite bundle.
- Verified the Phase 4 engineering gate in CI: Windows build/SQLite/DPAPI tests and Ubuntu clean resets plus all 174 Supabase/RLS/gateway/Realtime tests passed.

### Added (Phase 3 — server contract)

- Pinned Supabase CLI project tooling and local-stack configuration with invite-only authentication defaults.
- Kept the email authentication provider enabled while globally blocking public signups, allowing invited and admin-created users to sign in.
- Ordered Postgres migrations for the eight-table schema, constraints, indexes, RLS, explicit Data API grants, audit/profile/last-admin triggers, and Realtime publication.
- Idempotent local seed data for AQI, a complete empty week schedule, and a sample Normal Day timetable.
- GitHub Actions jobs for Windows .NET validation and repeatable Ubuntu Supabase migration/seed resets.
- Release-blocking HTTP-level RLS matrix covering every table, persona, and operation, plus behavioural tests for auth/profile triggers, immediate deactivation, audit images, FK/cascade rules, last-admin protection, and Realtime publication membership.
- Verified the Phase 3 engineering gate in CI: two clean database resets and all 173 Supabase integration tests passed.

### Security (Phase 3)

- Isolated security-definer functions in a private schema with locked search paths and restricted execution.
- Denied anonymous Data API table access and kept deactivated-user lockout in the database authorization path.
- Kept service-role credentials out of source and CI configuration.

### Added (Phase 2)

- Full planning documentation set under `docs/` (specification, architecture, database, UI flows, security, business rules, decisions ADR-001…ADR-014) and dependency-ordered `TASKS.md`.
- Domain entities: `Timetable`, `Period`, `WeekSchedule`, `DateOverride`, `Announcement`, `Profile`.
- Pure `ScheduleEngine`: effective-day resolution (override → week schedule → none), current/next period with overlap rules, 60-day next-lesson lookahead, notification-event derivation with end-warning suppression.
- `IClock` abstraction with `SystemClock` implementation registered in DI.
- 51 new unit tests covering the normative edge cases in ARCHITECTURE.md §4/§7 (54 total across the solution).
- Serilog rolling file logging to `%LOCALAPPDATA%\AqiClock\logs` (7-day retention) with a startup log entry.

### Changed (Phase 2)

- Replaced the `Microsoft.AspNetCore.App` framework reference with proper NuGet packages (`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Options.*`) per ADR-001's Phase 2 note.
- Added `CommunityToolkit.Mvvm` to the App and Application projects.

### Added

- Initial .NET 8 WPF solution and layered projects.
- Dependency injection, JSON structured logging, and typed configuration foundation.
- Domain, application, and integration test projects.
- Repository configuration and credential-safe environment example.

### Verification

- Debug solution build succeeds with 0 warnings and 0 errors.
- All 3 initial tests pass (domain, application, and integration).
