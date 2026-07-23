# Changelog

All notable changes to this project will be documented in this file.

## 0.10.0 - 2026-07-23

### Added

- Added audience-aware entry for credentialled teachers and admins or
  credential-free student sessions with one or more selected class audiences.
- Added class/audience administration, period tagging, class-targeted
  announcements, scheduled publication, and optional HTTPS e-Masjid links.

### Changed

- Rethemed the application with the Navy/Cream palette in Light and Dark modes.
- Renamed the announcement archive to **Scheduled & history** so future
  announcements are not mistaken for deleted items.

### Fixed

- Made sync stop quietly on sign-out and fully restart its heartbeat, Realtime
  subscription, and immediate cache refresh after every later sign-in.
- Initialized cached lessons and announcements when entering a student session.
- Based scheduled-announcement expiry presets on publication time and rejected
  custom expiry values that precede publication.
- Painted the role-choice and student-class-picker content with the active
  palette and matched the native main-window border to its theme surface.
- Surfaced unknown period-class tags in the Classes/Audiences banner as well as
  the edited row.

## 0.9.6 - 2026-07-20

### Fixed

- The Admin `DataGrid` surfaces now use the active card, text, divider, and
  selection brushes instead of exposing WPF's default white grid in Dark mode.
- Restored the main window's dynamic `WindowBrush` after WPF-UI updates its
  native titlebar, preventing the client margin from appearing as a dark frame
  in Light mode.
- Kept locally styled Fluent actions based on WPF-UI's implicit button style,
  removing default white pills from "Edit timetables" and
  "Compose / manage".

## 0.9.5 - 2026-07-18

### Added

- Redesigned the main window to the approved concept with a two-pane
  clock/current-lesson and today's-periods layout, explicit past/current/
  upcoming row states, Fluent symbol toolbar, state-coloured sync strip,
  empty-day treatment, and a theme-aware header mark.
- Added compact-only `HH:mm` time and inline lesson/countdown detail while
  preserving the fixed frameless 320×80 compact-window contract.
- Added minute-level relative sync details and focused recovery regressions
  for failed Realtime startup, later subscription attachment, and unexpected
  heartbeat refresh failures.

### Changed

- Changed the normal main-window baseline to 820×560 (700×500 minimum) so the
  approved two-pane information architecture remains usable when resized.
- Made the Realtime subscription an independently retried enhancement: REST
  cache initialization and the heartbeat now start even when the WebSocket
  upgrade fails.

### Fixed

- Prevented a Realtime WebSocket 403 from permanently wedging REST sync and
  heartbeat startup.
- Prevented the bundled C# client from sending a modern opaque
  `sb_publishable_*` key as a legacy Realtime Bearer token; the socket now uses
  its valid `apikey` query parameter and the user JWT only for channel joins.
- Kept the heartbeat alive after exception types outside the narrow
  HTTP/timeout/I/O filter, allowing later refreshes to recover connectivity.
- Removed the white ring around the dark main-window interior by painting the
  plain WPF window itself with the active `WindowBrush`.
- Prevented second-instance exit APPCRASH events by releasing the named mutex
  only from the process that actually acquired it.
- Kept the announcements flyout inside the normal-layout content row and
  separated its title/actions into explicit columns, preventing header
  collisions and status-bar overhang.

## 0.9.4 - 2026-07-18

### Fixed

- Restored readable inherited foregrounds throughout the plain-WPF main window
  in Dark mode and synchronized its native titlebar with live Light/Dark theme
  changes while preserving the frameless 320×80 compact-mode contract.

## 0.9.3 - 2026-07-18

### Changed (Fluent UX polish)

- Adopted WPF-UI 4.3 across sign-in, password recovery, Settings, Admin, and the main-window interior while preserving the accepted MVVM, native admin-grid, and compact-window behavior.
- Added Fluent light/dark/system theme plumbing with a navy brand accent, semantic error/warning resources, shared typography tokens, and targeted app-dictionary swapping that preserves framework resources.
- Enabled PerMonitorV2 DPI awareness and Mica-capable Fluent window chrome with graceful Windows 10 fallback.
- Surfaced the quill-and-inkwell branding in authentication, Settings/About, the main header, executable, tray, installer, and notification identity.
- Reworked Settings into Fluent cards and toggles, polished the six-tab Admin editor, and added professional clock, schedule, status, badge, and announcement presentation.

### Added (Phase 8 — packaging and pilot delivery)

- Tag-derived MinVer assembly versions surfaced in Settings/About with live updater state.
- Velopack-first WPF entry point, public GitHub release-source updater, six-hour checks, silent downloads, and apply-after-exit behaviour.
- Stable Velopack-stub resolution for the HKCU startup value, plus packaged icon lookup with a safe placeholder fallback.
- Reusable CI gates and a tagged release workflow that injects only public cloud configuration, builds self-contained `win-x64`, creates per-user full/delta packages, and targets the public releases-only repository.
- Cancel-path tests for timetable and override deletion and user role/activation changes, plus version, update-text, and startup-path tests.
- Pilot install/update/uninstall acceptance steps and the cloud/release-channel owner runbook.
- Conditional application-icon packaging that embeds and publishes `assets/app.ico` when the owner-supplied branding is available while preserving the placeholder fallback.

### Security (Phase 8)

- Release publishing fails closed unless HTTPS cloud configuration and a release-repository-scoped CI credential are present; no service-role or release token enters the client.
- Recorded ADR-017 for cross-repository publishing after confirming GitHub's built-in token cannot write to a separate repository.
- Bootstrapped the Free-tier production Supabase project from the frozen migrations, disabled global/email signup, enforced a 10-character minimum password, verified anonymous Data API denial, and created the first active administrator without using a service-role credential in the client or CI.
- Tracked leaked-password protection for the post-pilot Pro-plan review because Supabase does not expose it on the Free tier.

### Fixed (Phase 8 packaging acceptance)

- Made the sign-in window larger, resizable, and vertically scrollable so Windows DPI/text scaling cannot hide the credential controls in an unrecoverable fixed-size window.
- Replaced the unusable default `localhost:3000` password-recovery destination with a Velopack-managed `aqiclock://reset-password` activation, a dedicated password-update window, secure forwarding to an existing single instance, and in-memory-only recovery-token handling.
- Packaged the supplied quill-and-inkwell branding for the executable, installer, tray, and Windows notifications; exposed the selected end-warning minutes beside its slider; and made the main-window action buttons consistently sized.

### Added (Phase 7 — admin editing)

- Online-only, role-gated admin workspace for timetables and ordered periods, weekly assignments, date overrides, announcement publishing, recent audit history, and profile role/activation management.
- Client validation for period bounds and duplicate names, non-blocking overlap warnings, referenced-timetable explanations, duplicate-date replacement, and courtesy Reload/Overwrite conflict handling.
- Typed Supabase write failures for denied, referenced, duplicate, and last-admin outcomes, plus online audit retrieval and administrator profile updates.
- Targeted cache refreshes after successful writes, live demotion closure, and an admin compose/manage entry point in the announcements panel.
- Admin ViewModel and cache-identity regression tests plus live gateway coverage for profile, week-schedule, and audit operations; current totals are 100 non-Supabase tests and 177 live Supabase tests.
- Verified the Phase 7 automated gate in CI: the Windows build/test job and repeatable-reset Supabase RLS/gateway job both passed.

### Fixed (Phase 7 runtime smoke)

- Marshalled connectivity, session, and Realtime-driven editor updates onto the WPF dispatcher so heartbeat or socket callbacks cannot update bound commands and collections from a worker thread.
- Bound cached profile identifiers consistently as GUIDs so a freshly synced administrator is recognized immediately instead of being presented as Staff.
- Prevented timetable conflict prompts from latching on the editor's own Realtime echo; Reload and successful Save now deterministically clear dirty/conflict state and retain the edited selection.
- Made week-schedule and override selectors explicitly update their row ViewModels on selection, and made critical profile/schedule PATCH calls fail loudly when the server updates zero rows.
- Ordered the Users view with the signed-in user and administrators first, preserved the signed-in email, labelled unavailable MVP email data accurately, and made role/active bindings explicitly two-way.
- Added a debug WPF binding-error listener routed through structured logging plus an STA rendered-window regression that exercises all admin tabs, both timetable selectors, and Users cell/role rendering with zero binding failures.
- Humanized announcement, profile, override-date, and weekday audit entries instead of falling back to raw identifiers where descriptive fields exist.
- Verified the P7-1–P7-3 regression package in both release-blocking CI jobs, including the rendered WPF binding test and all 177 live Supabase tests.

- Made live administrator demotion atomically close the open editor and show its role-change explanation, avoiding messenger-recipient ordering races.
- Added confirmation prompts before deleting timetables (and their periods), overrides, or announcements, and before changing a user's role or activation state.
- Completed focused Phase 7 visual acceptance: user-role round-trip, duplicate-date replacement, referenced-delete handling, J7 live demotion, and staff-only gating all passed against the local stack.

### Added (Phase 6 — ambient Windows integration)

- In-process notification scheduler implementing the 120-second grace window, persistent fired/skipped dedup, schedule rebuilds, moved-boundary re-fire, end-warning suppression, announcement first-sighting, and per-category settings.
- Native Windows lesson, end-warning, announcement, and test toasts with activation routing to the main window or announcements panel.
- Signed-in-only tray residence with live schedule tooltip and the S11 Open, compact, pin, announcements, sync, Settings, sign-out, and Exit menu.
- Idempotent per-user Windows startup registration with `--minimized`, plus tray-only restored-session startup.
- ADR-011 manual Windows checklist covering tray lifecycle, toast activation, auto-start/reboot, sleep/resume grace, and DST-date checks.
- Scheduler, SQLite notification-log, and native tray-icon regressions; the local suite now contains 92 non-Supabase tests and all 175 live Supabase tests remain green.

### Changed (Phase 6 — platform contract)

- Targeted the WPF app and UI tests at Windows 10 build 17763 or later so unpackaged native toast activation is available; recorded as ADR-016.

### Fixed (Phase 6 — visual acceptance)

- Configured H.NotifyIcon through its native `System.Drawing.Icon` property, avoiding the unsupported `InteropBitmap` conversion that left the tray icon blank and raised a global error dialog.
- Documented Windows Do Not Disturb/Focus Assist routing successful notifications into Notification Center instead of showing banners.
- Marshalled tray creation/removal triggered by `SessionChanged` onto WPF's STA dispatcher, so successful interactive authentication no longer fails while constructing the tray menu on a continuation thread.
- Split sign-in authentication, initial-sync, and window-activation error boundaries; every handled failure is now structured-logged and only an HTTP 400/401 from the authentication stage is labelled as incorrect credentials.
- Observed and logged restored-session, heartbeat, and network-change background sync faults instead of leaving fire-and-forget tasks silent.
- Added regressions for worker-thread tray transitions and post-auth sync error messaging, plus a Windows-targeted live smoke covering password sign-in, Realtime subscription/change delivery, and the resulting SQLite cache refresh.

### Added (Phase 5 — staff UI)

- Generic Host WPF composition root with single-instance activation, application lifetime wiring, structured global exception handling, and window navigation behind `IWindowService`.
- One-second dispatcher clock publishing `ClockTick` and resume/clock-change `TimeJumped` messages.
- Invite-only sign-in and password-reset UI, initial-sync progress, authenticated startup routing, and shared-machine sign-out routing.
- Normal and compact clock layouts using the Domain schedule engine over SQLite-backed repositories, including current/next lesson, countdown, progress, day list, connectivity status, admin placeholder, pinning, and per-mode placement.
- Read-only announcements panel with poster names, relative times, unread counts, expiry filtering, and local read state.
- Typed JSON settings, light/dark/system themes, notification preferences, account controls, and log-folder access.
- ViewModel and settings tests plus a live Supabase password-recovery smoke test; final Phase 5 totals are 82 non-Supabase and 175 live Supabase tests.
- Verified the Phase 5 automated gate in CI: Windows build/ViewModel tests and Ubuntu clean resets plus all 175 Supabase tests passed.

### Fixed (Phase 5 — J1 acceptance)

- Prevented initial sign-in command evaluation from throwing on empty or whitespace email input.
- Initialized the disposable SQLite cache before session restore/sign-in can query a cached profile, allowing the role to arrive later with the Profiles snapshot on a true first run.
- Kept unexpected local persistence failures inside the sign-in error state instead of escalating them to the global crash dialog.
- Added seam-level regressions for blank-email command gating, unexpected persistence errors, and real fresh-file cache initialization during sign-in.
- Applied compact mode as an explicit fixed 320×80 frameless window presentation, avoiding WPF local-value precedence over style triggers.
- Stopped sign-in routing from constructing a hidden main window; closing sign-in while signed out now exits cleanly, and signing out closes Settings before returning to sign-in.
- Serialized atomic settings writes and debounced placement persistence so rapid resize/drag/mode events cannot race over `settings.json.tmp`.
- Completed Phase 5 visual acceptance on `fe7e526`: cold-start J1, Realtime refresh, announcements/read state, Settings/sign-out, compact/normal persistence, dragging, and signed-out process exit all passed without new log errors or crash dialogs.
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
