# AQI Clock implementation tasks

Ordered by implementation dependency: each phase builds only on the phases above it. Specifications: see `docs/`. Decision references (ADR-x) are in `docs/DECISIONS.md`; business inputs (B-x) in `docs/SPECIFICATION.md` §5.

## Phase 1 — Foundation ✅ (complete)

- [x] Initialise the .NET solution and required projects.
- [x] Add clean project references.
- [x] Enable nullable reference types and strict warning handling.
- [x] Configure dependency injection.
- [x] Configure structured logging.
- [x] Add strongly typed, validated configuration.
- [x] Add unit and integration test projects.
- [x] Add repository ignore rules and environment example.
- [x] Verify solution build (0 warnings, 0 errors).
- [x] Verify initial test suite (3 passed, 0 failed).

## Phase 2 — Domain: schedule engine (no I/O, pure logic) ✅ (complete)

- [x] Replace the Phase 1 `Microsoft.AspNetCore.App` framework reference with `Microsoft.Extensions.Hosting` NuGet packages + Serilog file sink (ADR-001 Phase 2 note).
- [x] Add `CommunityToolkit.Mvvm` to App/Application projects.
- [x] Domain entities: `Timetable`, `Period`, `WeekSchedule`, `DateOverride`, `Announcement`, `Profile` (wall-clock times use BCL `TimeOnly`/`DateOnly`).
- [x] `IClock` abstraction (`Now`, `LocalToday`) + `SystemClock` in Infrastructure, registered in DI.
- [x] Effective-timetable resolver (override → weekday → none) per ARCHITECTURE.md §4.
- [x] Current/next period computation incl. overlap rule (latest start wins, sort_order tiebreak).
- [x] Next-school-day scan (60-day lookahead across overrides and closed days).
- [x] Notification-event derivation (start events, end-warning events with short-period suppression).
- [x] Unit tests for every §4/§7 edge case: overlaps, DST spring-forward date, closed days, empty schedule, zero-length/inverted periods, mid-day recompute, lookahead boundary, key stability (51 tests).

## Phase 3 — Supabase backend (schema before any client code that uses it)

- [ ] Create Supabase project; record URL + anon key in `.env.example` shape (B-6 region/tier).
- [x] `supabase/` folder with pinned CLI config; migrations under `supabase/migrations/`.
- [x] Migration 1: tables per DATABASE.md §1 (organizations, profiles, timetables, periods, week_schedule, date_overrides, announcements, audit_log) + indexes + CHECK constraints + `set_updated_at()` trigger.
- [x] Migration 2: hardened `current_org_id()` / `is_admin()` helpers + explicit Data API grants + RLS policies per SECURITY.md §3.
- [x] Migration 3: audit triggers, `on_auth_user_created` profile trigger, profile column-guard trigger, last-admin guard trigger, and Realtime publication.
- [x] `seed.sql`: one organization, 7 week_schedule rows, sample "Normal Day" timetable.
- [ ] Cloud: disable public signups, enable leaked-password protection, and set minimum password length 10. (Local config already mirrors invite-only signup and minimum length.)
- [ ] Bootstrap first admin user via dashboard; document the exact steps in README.
- [ ] Integration test harness against `supabase start` (local stack) with per-role JWTs.
- [ ] RLS test matrix: each table × {anon, staff, admin, deactivated} × {select, insert, update, delete} — release-blocking in CI.
- [ ] Audit trigger tests (before/after images, actor capture); last-admin guard test; RESTRICT delete tests.
- [x] CI scaffold: Windows build/tests and Ubuntu clean/repeated Supabase reset. RLS/behaviour tests remain required before the Phase 3 gate is green.

## Phase 4 — Infrastructure: cache, auth, sync (depends on Phases 2–3)

- [ ] SQLite cache per DATABASE.md §3: migration runner, mirror tables, `meta`/`sync_state`/`notification_log`/`announcement_read`, WAL mode, corruption recovery (delete + re-pull).
- [ ] Repositories over SQLite implementing the Application interfaces.
- [ ] Supabase client wrapper (`Supabase` NuGet): auth (sign-in, refresh, sign-out), table pulls, admin writes.
- [ ] DPAPI-encrypted session persistence; wipe-on-sign-out (cache + session + local state).
- [ ] `SyncService`: connectivity state machine, snapshot pull per table, Realtime subscription with 500 ms debounce, `Sync now`, clock-skew check (ARCHITECTURE.md §6, §8).
- [ ] `SessionService` exposing auth state + role from cached profile.
- [ ] SQLite tests: snapshot-replace transactionality, migrations, notification_log pruning, org-mismatch cache wipe.

## Phase 5 — App: main UI (depends on Phase 4)

- [ ] Generic Host composition root in `App.xaml.cs`; single-instance mutex; global exception handler.
- [ ] `ClockService` (1 s tick, `TimeJumped` detection).
- [ ] Sign-in window + initial-sync flow (UI-FLOWS.md S1/J1), incl. cache-present fast path and first-run-offline empty state.
- [ ] Main window Normal mode (S2): clock, current lesson card, next lesson, today's list, status strip.
- [ ] Compact mode (S3) + always-on-top + window state persistence (positions validated against screen bounds).
- [ ] Announcements panel (S4) view side: list, unread badge, local read-state.
- [ ] Settings window (S5) + JSON `SettingsService`.
- [ ] Theme support (light/dark/system).
- [ ] ViewModel unit tests: formatting, state transitions, offline command gating.

## Phase 6 — App: tray, startup, notifications (depends on Phase 5)

- [ ] Tray icon + menu + live tooltip (H.NotifyIcon); close-to-tray behaviour; Exit vs Open semantics (S11).
- [ ] `StartupService`: HKCU Run key, start-minimised handling.
- [ ] Toast infrastructure (`ToastNotificationManagerCompat`, AUMID, activation → window/panel).
- [ ] `NotificationScheduler`: firing rules 1–7 of ARCHITECTURE.md §7 (grace window, dedup log, rebuild on `DataChanged`/`TimeJumped`, announcement sighting rule).
- [ ] "Send test notification" in Settings.
- [ ] Unit tests for scheduler rules with fake clock; manual test checklist doc for toasts/tray/sleep-resume (`docs/MANUAL-TESTS.md`).

## Phase 7 — App: admin editing (depends on Phases 5–6; server already enforces roles)

- [ ] Admin window shell, reachable only when role=admin; live role-change handling (J7).
- [ ] Timetables tab (S6): CRUD, reorder, duplicate, archive; validation (block end≤start & duplicate names, warn overlaps); delete-blocked explanations.
- [ ] Week schedule tab (S7).
- [ ] Date overrides tab (S8) incl. duplicate-date replace flow.
- [ ] Concurrent-edit courtesy prompt ("changed by <name> — reload?").
- [ ] Announcement compose/delete (admin side of S4) with expiry presets.
- [ ] Recent changes tab (S9): online-only audit list (last 100).
- [ ] Users tab (S10): role toggle + deactivate (server guards last admin).
- [ ] ViewModel tests: validation, role gating, conflict prompt.

## Phase 8 — Packaging, CI/CD, rollout (depends on everything above)

- [ ] GitHub Actions CI: build + unit tests (windows-latest), Supabase RLS integration tests (ubuntu-latest, release-blocking).
- [ ] Velopack packaging: per-user installer, Start-menu shortcut, app icon (B-8).
- [ ] Auto-update: startup + 6 h check, apply-on-restart, About-screen status (J9).
- [ ] Release pipeline to GitHub Releases (B-7).
- [ ] Run full manual test checklist on Win10 + Win11, incl. sleep/resume, offline day, DST-date simulation.
- [ ] Pilot install on 3–5 staff machines; collect logs/feedback.
- [ ] Confirm business inputs B-1 … B-8 with owner.
- [ ] **Pre-wide-rollout gate:** code-signing certificate acquired and signing wired into the pipeline (ADR-010); Supabase tier reviewed for Realtime connection limits (B-6); .NET upgrade to current LTS scheduled (.NET 8 EOL Nov 2026, ADR-002).

## Post-MVP backlog (unordered — see SPECIFICATION.md §4)

- [ ] Per-user personal timetables.
- [ ] In-app user invitations.
- [ ] Rich audit viewer (filters, diffs, export).
- [ ] Announcement targeting/scheduling/acknowledgement.
- [ ] Week view, printing, ICS export.
- [ ] Synced preferences; localisation (Arabic/RTL); kiosk display mode.
- [ ] Multi-organisation UI.
