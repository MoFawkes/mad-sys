# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

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
