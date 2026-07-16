# AQI Clock

AQI Clock is a Windows desktop application (WPF, .NET 8) that shows staff the current time, the current lesson, time remaining, and the next lesson, driven by a centrally managed timetable. Administrators edit timetables and post announcements in Supabase-backed storage; every staff machine updates in near-real-time, works offline from a local SQLite cache, and raises native Windows notifications at lesson boundaries.

**Status:** Phases 1–7 are complete and acceptance-green. Phase 8 packaging and release engineering is implemented locally; pilot publication remains blocked on the cloud Supabase project, public release repository credentials, and branding asset. See [`TASKS.md`](TASKS.md) for the phased plan and [`docs/MANUAL-TESTS.md`](docs/MANUAL-TESTS.md) for Windows integration acceptance.

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

## Pilot installation and updates

Pilot installers are published as unsigned Velopack assets in the public `MoFawkes/aqi-clock-releases` repository. Download `AqiClock.App-stable-Setup.exe`, run it as the staff user, and expect Windows SmartScreen to warn until code signing is added before wide rollout. Installation is per-user and creates a Start-menu shortcut without elevation.

The installed app checks that public repository at startup and every six hours. It downloads updates silently and applies a prepared update after the app next exits; the following launch uses the new version. Settings → About shows the current tag-derived version and update state. `%LOCALAPPDATA%\AqiClock` remains outside Velopack's versioned application directory, so cache, session, settings, and logs survive updates.

## Cloud project bootstrap (owner)

The pilot build must not be tagged until these steps are complete:

1. Run `npx supabase login`, create the Free-tier project, then `npx supabase link --project-ref <ref>`.
2. Run `npx supabase db push` to apply the frozen migrations.
3. In the dashboard SQL editor, insert the production organisation row (do not run the local fixture seed wholesale).
4. Disable public signups, invite the first administrator under Authentication, then set the generated profile's role to `admin` in the SQL editor.
5. Add the public project URL and anon key as repository variables `CLOUD_SUPABASE_URL` and `CLOUD_SUPABASE_ANON_KEY`. Never provide a service-role key.

The release workflow also requires a fine-grained Actions secret named `RELEASES_TOKEN`, limited to contents-write access on `MoFawkes/aqi-clock-releases`. GitHub's source-repository token cannot publish into a different repository. This CI credential is never bundled into the client.

## Creating a release

Release versions come from `v`-prefixed SemVer tags through MinVer. The tag workflow reruns both release-blocking CI jobs, injects the public cloud configuration, publishes a self-contained `win-x64` build, creates full/delta Velopack packages, and uploads them to the public assets repository.

```powershell
git tag v0.9.0
git push origin v0.9.0
```

Development builds keep updates disabled unless `AQICLOCK_Updates__RepositoryUrl` is explicitly set.
