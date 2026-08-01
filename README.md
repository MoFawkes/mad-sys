# AQI Clock

AQI Clock is a Windows tray application with an Expo mobile companion. Teachers and students see the current lesson, time remaining, next lesson, today's timetable, and targeted announcements from a centrally managed Supabase schedule. Both clients render from local SQLite snapshots and remain useful offline.

The desktop presentation uses WPF-UI Fluent controls, light/dark/system themes, a navy brand accent, Mica-capable window chrome on Windows 11, and PerMonitorV2 scaling. MainWindow intentionally retains native WPF chrome switching so its accepted 320×80 frameless compact mode remains stable.

**Status:** Windows v0.10.0 is live. The v0.11.0 Expo companion is implemented and awaits physical-device/manual acceptance; Android notification drift and hosted Auth configuration are release gates. See [`PROJECT-STATUS.md`](PROJECT-STATUS.md) and [`docs/MANUAL-TESTS.md`](docs/MANUAL-TESTS.md).

## Key capabilities

- Desktop clock with current lesson, countdown, and next lesson
- Compact always-on-top mode, system tray residence, automatic Windows startup
- Multiple timetable types, weekly schedule, and date-specific overrides
- Native Windows notifications and OS-scheduled mobile lesson notifications
- Teacher and personal student-phone paths with targeted announcements
- Admin editing with role-based permissions and server-side audit history
- Admin-only student join-code distribution with desktop QR, rotation, and device revocation
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
| [`PROJECT-STATUS.md`](PROJECT-STATUS.md) | Live Architecture / Engineering handoff, release state, ownership, and risks |
| [`TASKS.md`](TASKS.md) | Dependency-ordered implementation checklist |

## Repository layout

```
src/
  AqiClock.Domain/          Entities and the pure schedule engine
  AqiClock.Application/     Interfaces, options, use-case services
  AqiClock.Infrastructure/  SQLite cache, Supabase client, DPAPI sessions, sync
  AqiClock.App/             WPF composition root, views, ViewModels, settings and themes
tests/                      Domain, Application, and integration test projects
mobile/                     Expo SDK 54 companion, SQLite cache, notifications, Jest tests
docs/                       Planning and architecture documentation
supabase/                   SQL migrations and seed — source of truth for the server schema
```

## Prerequisites

- Windows 10 (1809+) or Windows 11
- .NET 8 SDK
- Node.js 20.19.x for the Expo app

## Build and test

```powershell
dotnet build AqiClock.sln
dotnet test AqiClock.sln --no-build
cd mobile
npm ci
npm test -- --runInBand
npx tsc --noEmit
npx eslint .
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

Mobile preview builds load the hosted project URL and client-safe publishable key from `mobile/.env`; these `EXPO_PUBLIC_*` values are intentionally embedded in the APK. Desktop uses the `AQICLOCK_` names. Never place a service-role/secret key in either client or `.env` file.

## Pilot installation and updates

Pilot installers are published as unsigned Velopack assets in the public `MoFawkes/aqi-clock-releases` repository. Download `AqiClock.App-stable-Setup.exe`, run it as the staff user, and expect Windows SmartScreen to warn until code signing is added before wide rollout. Installation is per-user and creates a Start-menu shortcut without elevation.

The installed app checks that public repository at startup and every six hours. It downloads updates silently and applies a prepared update after the app next exits; the following launch uses the new version. Settings → About shows the current tag-derived version and update state. `%LOCALAPPDATA%\AqiClock` remains outside Velopack's versioned application directory, so cache, session, settings, and logs survive updates.

## Cloud project bootstrap (owner)

The production project was bootstrapped on 2026-07-17 using this runbook:

1. Run `npx supabase login`, create the Free-tier project, then `npx supabase link --project-ref <ref>`.
2. Run `npx supabase db push` to apply the frozen migrations.
3. In the dashboard SQL editor, insert the production organisation row (do not run the local fixture seed wholesale).
4. Enable global signup and anonymous sign-ins, register `private.before_user_created` as the Before User Created hook, and verify public email signup is rejected. Keep the email provider enabled.
5. Add the public project URL and anon key as repository variables `CLOUD_SUPABASE_URL` and `CLOUD_SUPABASE_ANON_KEY`. Never provide a service-role key.
6. Under Authentication → URL Configuration, add both `aqiclock://reset-password` and `aqiclock-mobile://reset-password`.
7. Create the first administrator through the Admin API/dashboard, then set the generated profile role to `admin`.

For the current Free-tier pilot, leaked-password protection remains unavailable. The signup hook replaces the old disabled-global-signup control and is release-gated by integration tests. Unenrolled anonymous users read no application rows.

The release workflow also requires a fine-grained Actions secret named `RELEASES_TOKEN`, limited to contents-write access on `MoFawkes/aqi-clock-releases`. GitHub's source-repository token cannot publish into a different repository. This CI credential is never bundled into the client.

## Creating a release

Release versions come from `v`-prefixed SemVer tags through MinVer. The tag workflow reruns both release-blocking CI jobs, injects the public cloud configuration, publishes a self-contained `win-x64` build, creates full/delta Velopack packages, and uploads them to the public assets repository.

```powershell
git tag v0.9.0
git push origin v0.9.0
```

Development builds keep updates disabled unless `AQICLOCK_Updates__RepositoryUrl` is explicitly set.

Password-recovery emails return to the installed app through `aqiclock://reset-password`. Velopack registers that current-user protocol during install/update and removes it during uninstall. Recovery tokens are used only in memory to update the password and are never stored in AQI Clock's session, cache, settings, or logs.
