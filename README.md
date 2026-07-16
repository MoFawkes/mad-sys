# AQI Clock

AQI Clock is a Windows desktop application (WPF, .NET 8) that shows staff the current time, the current lesson, time remaining, and the next lesson, driven by a centrally managed timetable. Administrators edit timetables and post announcements in Supabase-backed storage; every staff machine updates in near-real-time, works offline from a local SQLite cache, and raises native Windows notifications at lesson boundaries.

**Status:** Phases 1–5 are complete and CI-green. The staff-facing WPF UI has passed full visual acceptance; Phase 6 tray, startup, and notification work is unblocked. See [`TASKS.md`](TASKS.md) for the phased plan.

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
  AqiClock.Infrastructure/  SQLite cache, Supabase client, DPAPI sessions, sync
  AqiClock.App/             WPF composition root, views, ViewModels, settings and themes
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

## Supabase integration tests

The RLS, behavioural, and gateway smoke tests require Docker Desktop and a running local Supabase stack. They skip automatically when `SUPABASE_URL` is absent.

```powershell
npx supabase start
npx supabase db reset --local --yes
$status = npx supabase status -o json | ConvertFrom-Json
$env:SUPABASE_URL = $status.API_URL
$env:SUPABASE_ANON_KEY = $status.ANON_KEY
$env:SUPABASE_SERVICE_ROLE_KEY = $status.SERVICE_ROLE_KEY
$env:SUPABASE_DB_URL = $status.DB_URL
dotnet test tests/AqiClock.SupabaseTests/AqiClock.SupabaseTests.csproj --configuration Release
```

These are disposable local-stack credentials scoped to the current shell; never store a cloud service-role key in source or CI configuration.

To launch the WPF app against the local stack, set the client-safe URL and anon key in the same shell before starting it:

```powershell
$env:AQICLOCK_Supabase__Url = $status.API_URL
$env:AQICLOCK_Supabase__AnonKey = $status.ANON_KEY
dotnet run --project src/AqiClock.App/AqiClock.App.csproj --configuration Release
```

Without these overrides, the checked-in placeholder configuration intentionally cannot authenticate.

## Configuration

Copy `.env.example` values into your preferred local environment-variable mechanism. The application reads variables prefixed with `AQICLOCK_`; use a double underscore for nested configuration keys. Only the Supabase project URL and **anon** key belong in client configuration — never commit credentials, and never place the service-role key anywhere in this repository or the app (see `docs/SECURITY.md`).
