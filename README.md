# AQI Clock

AQI Clock is a Windows desktop application (WPF, .NET 8) that shows staff the current time, the current lesson, time remaining, and the next lesson, driven by a centrally managed timetable. Administrators edit timetables and post announcements in Supabase-backed storage; every staff machine updates in near-real-time, works offline from a local SQLite cache, and raises native Windows notifications at lesson boundaries.

**Status:** Phase 1 (solution foundation) and Phase 2 (pure schedule engine + tests) complete. Next up: Phase 3, the Supabase schema and RLS. See [`TASKS.md`](TASKS.md) for the phased plan.

## Key capabilities (planned MVP)

- Desktop clock with current lesson, countdown, and next lesson
- Compact always-on-top mode, system tray residence, automatic Windows startup
- Multiple timetable types, weekly schedule, and date-specific overrides
- Native Windows toast notifications (lesson start, end warning, announcements)
- Admin editing with role-based permissions and server-side audit history; staff read-only
- Full offline operation with automatic resynchronisation via Supabase Realtime

## Documentation

| Document | Contents |
|---|---|
| [`docs/SPECIFICATION.md`](docs/SPECIFICATION.md) | MVP scope, roles, features, deferred items, open business decisions |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Stack, layering, schedule engine rules, sync, notifications, testing, packaging |
| [`docs/DATABASE.md`](docs/DATABASE.md) | Supabase schema, relationships, RLS summary, SQLite cache design |
| [`docs/UI-FLOWS.md`](docs/UI-FLOWS.md) | Every screen and user journey, UI edge-case behaviour |
| [`docs/BUSINESS_RULES.md`](docs/BUSINESS_RULES.md) | Plain-language "what happens when…" rules and precedence |
| [`docs/SECURITY.md`](docs/SECURITY.md) | Auth, RLS policies, client storage, transport/update security |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Architectural decision record (ADR-001 …) |
| [`TASKS.md`](TASKS.md) | Dependency-ordered implementation checklist |

## Repository layout

```
src/
  AqiClock.Domain/          Entities and the pure schedule engine
  AqiClock.Application/     Interfaces, options, use-case services
  AqiClock.Infrastructure/  (Phase 4) SQLite cache, Supabase client, sync
  AqiClock.App/             WPF application
tests/                      Domain, Application, and integration test projects
docs/                       Planning and architecture documentation
supabase/                   (Phase 3) SQL migrations and seed — source of truth for the server schema
```

## Prerequisites

- Windows 10 (1809+) or Windows 11
- .NET 8 SDK

## Build and test

```powershell
dotnet build AqiClock.sln
dotnet test AqiClock.sln --no-build
```

## Configuration

Copy `.env.example` values into your preferred local environment-variable mechanism. The application reads variables prefixed with `AQICLOCK_`; use a double underscore for nested configuration keys. Only the Supabase project URL and **anon** key belong in client configuration — never commit credentials, and never place the service-role key anywhere in this repository or the app (see `docs/SECURITY.md`).
