# AQI Clock — Application Architecture

Status: Draft 1.0 (planning) · Last updated: 2026-07-15

---

## 1. Technology stack (firm)

| Concern | Choice | Notes |
|---|---|---|
| Runtime | .NET 8 (LTS) | Already scaffolded in Phase 1; installed SDK 8.0.407. Supported to Nov 2026 — schedule an upgrade to the then-current LTS as a pre-wide-rollout task (ADR-002) |
| UI | WPF | Requirement; mature, good tray/window control |
| MVVM | CommunityToolkit.Mvvm 8.x | Source-generated `[ObservableProperty]`, `[RelayCommand]`, `IMessenger` |
| DI / hosting | Microsoft.Extensions.Hosting + DependencyInjection | Generic Host owns background services and lifetime |
| Backend | Supabase (Auth, Postgres, Realtime) | `Supabase` (supabase-csharp) NuGet client |
| Local cache | SQLite via Microsoft.Data.Sqlite | Hand-written SQL + tiny migration runner; **no ORM** (see DECISIONS.md ADR-005) |
| Notifications | Microsoft.Toolkit.Uwp.Notifications | Works from unpackaged desktop apps on Win10 1809+ |
| Tray icon | H.NotifyIcon.Wpf | Maintained successor to Hardcodet |
| Logging | Serilog → rolling file in `%LOCALAPPDATA%\AqiClock\logs` | 7-day retention |
| Tests | xUnit (plain asserts; NSubstitute added if/when test doubles are needed) | FluentAssertions dropped — v8 moved to a paid commercial licence. See §9 |
| Packaging/updates | Velopack | See §10 and DECISIONS.md ADR-010 |

Solution layout — **already scaffolded in Phase 1** (`AqiClock.sln` exists; the `supabase/` folder is added in Phase 2):

```
AqiClock.sln
src/
  AqiClock.Domain/          # Entities, schedule engine, notification-event derivation. BCL only.
  AqiClock.Application/     # Use-case services, repository/service interfaces, options, ViewModels' contracts.
  AqiClock.Infrastructure/  # SQLite cache, Supabase client wrapper, sync service, DPAPI session store.
  AqiClock.App/             # WPF: views, viewmodels, tray, notifications, settings, DI composition root.
tests/
  AqiClock.Domain.Tests/
  AqiClock.Application.Tests/
  AqiClock.IntegrationTests/   # DI wiring, SQLite, and (Phase 2+) local-Supabase RLS tests
supabase/
  migrations/               # SQL migrations (source of truth for server schema)
  seed.sql
```

Dependency rule (per ADR-001): `App → Application + Infrastructure`, `Infrastructure → Application + Domain`, `Application → Domain`. `Domain` references nothing but the BCL. All time access in Domain/Application goes through an injected `IClock` (`Now`, `LocalToday`) so the schedule engine is fully unit-testable.

Where responsibilities land in this layering: the **schedule engine** (§4) is pure Domain; sync/notification/session orchestration are Application services with their I/O behind interfaces; Supabase, SQLite, DPAPI, registry, and toast APIs live in Infrastructure/App only. Phase 2 replaces the Phase 1 `Microsoft.AspNetCore.App` framework reference with the proper `Microsoft.Extensions.Hosting` NuGet packages (a WPF desktop app should not ship the ASP.NET Core shared framework) — tracked in TASKS.md.

---

## 2. Process architecture

Single WPF process hosted in a Generic Host. Long-running work is done by hosted services registered in DI:

| Service | Responsibility |
|---|---|
| `ScheduleEngine` (Domain) | Pure functions: given cached data + a `DateTime`, compute effective timetable for a date, current period, next period, time remaining, and the ordered list of upcoming notification events |
| `ClockService` | 1 s `DispatcherTimer` tick → publishes `ClockTick`; detects system resume/clock jumps (tick gap > 5 s) and publishes `TimeJumped` |
| `SyncService` | Initial pull, reconnect pull, Realtime subscription, connectivity state machine (§6) |
| `NotificationScheduler` | Consumes schedule + ticks, fires toasts, dedupes via `notification_log` (§7) |
| `SessionService` | Auth state, token refresh, role exposure |
| `SettingsService` | Local JSON settings, change notifications |
| `TrayService` | Tray icon, tooltip updates, menu commands |
| `StartupService` | HKCU Run key management |

Communication between services and ViewModels uses `IMessenger` (weak-reference pub/sub): `ClockTick`, `TimeJumped`, `DataChanged(table)`, `ConnectivityChanged`, `SessionChanged`, `AnnouncementReceived`. ViewModels never talk to Supabase or SQLite directly — they depend on `ITimetableRepository`, `IAnnouncementRepository`, `ISyncService`, `ISessionService` interfaces defined in Application.

Single-instance enforcement: named mutex; a second launch activates the existing window and exits.

---

## 3. MVVM structure

| View | ViewModel | Notes |
|---|---|---|
| `MainWindow` | `MainViewModel` | Hosts clock display; switches Normal/Compact templates via a `DisplayMode` property |
| `ClockView` (usercontrol) | `ClockViewModel` | Time, current/next lesson, remaining, progress, today's list |
| `AnnouncementsView` | `AnnouncementsViewModel` | List + unread; admin compose section when role=Admin |
| `SignInWindow` | `SignInViewModel` | Email/password |
| `SettingsWindow` | `SettingsViewModel` | Local settings |
| `AdminWindow` (tabbed) | `AdminViewModel` + child VMs: `TimetableEditorViewModel`, `WeekScheduleViewModel`, `OverridesViewModel`, `AuditViewModel`, `UsersViewModel` | Only reachable when role=Admin |

Navigation: window-based (no frame navigation). A `WindowService` abstraction opens/activates windows so ViewModels stay testable.

---

## 4. Schedule engine rules (timetable calculation)

All computation uses **local wall-clock time** in the organisation's timezone; period times are stored as time-of-day without timezone (see DECISIONS.md ADR-006).

**Effective timetable for date D:**
1. If a `date_override` exists for D: use its timetable, or "closed" if its timetable is null.
2. Else if the week schedule assigns a timetable to D's weekday: use it.
3. Else: no school.

**Current period at instant T** (period is active when `start ≤ T < end`):
- If multiple periods are active (overlap), pick the one with the **latest start**; tie-break by lowest `sort_order`. Rationale: the most recently started activity is what's actually happening.
- Overlaps are warned about at edit time but must never crash or confuse the runtime.

**Next period at instant T:** the period with the smallest `start > T` today; if none, resolve the next school day (scan forward up to 60 days through overrides + week schedule; beyond that show "No upcoming lessons").

**Time remaining:** `current.end − T`, clamped at 0. Progress: `(T − start) / (end − start)`.

**Edge cases (normative):**
- **Zero-length or inverted period** (end ≤ start): rejected at edit time; if present in cache anyway (bad legacy data), treated as never active.
- **Midnight**: periods cannot cross midnight (edit-time rule: end ≤ 23:59 same day). A school day is one calendar date.
- **DST transitions**: times are wall-clock, so lessons follow the local clock. On the spring-forward day a period spanning the skipped hour is simply shorter in real time; on fall-back, longer. The engine recomputes from `DateTime.Now` each tick and never does duration arithmetic across the transition, so display stays correct. Notification triggers are stored as local `DateTime` and re-derived after any `TimeJumped` event.
- **Timetable edited mid-day**: on `DataChanged`, the engine recomputes immediately. If the current period disappeared or changed times, the display updates on the next tick and all pending notifications are rebuilt (§7). No notification fires for a boundary that no longer exists.
- **App restart / resume mid-period**: pure recomputation from cache — the engine has no persisted runtime state, so restart is inherently safe. Notification dedup handles "did I already notify?" (§7).
- **Clock jumps** (manual change, NTP correction, resume): `TimeJumped` forces recompute + notification rebuild; the missed-notification grace rule (§7) governs firing.
- **Empty day / closed day**: display "No lessons today" + next school day's first lesson.

---

## 5. Data flow

```
Supabase Postgres --initial/reconnect pull--> SQLite cache --repositories--> ScheduleEngine --> ViewModels
        ^                                          ^                              |
        |<-- admin writes (online only) -----------+                              v
        +---- Realtime postgres_changes --> SyncService (triggers re-pull)  NotificationScheduler
```

- **Reads are always from SQLite.** The UI never renders directly from a network response; writes and Realtime events update Postgres, then the affected tables are re-pulled into SQLite, then `DataChanged` fires. One rendering path = fewer states to test.
- **Admin writes go directly to Supabase** and rely on the Realtime→re-pull loop to update the local cache (plus an immediate targeted re-pull after own writes so the editor feels instant).

---

## 6. Synchronisation and conflict rules

### Connectivity state machine
`Online ⇄ Offline` with `Syncing` as a transient. Detection: Realtime socket state + failure/success of Supabase calls + a 30 s heartbeat re-check while offline (exponential backoff capped at 5 min, plus immediate retry on Windows `NetworkAddressChanged`).

### Sync algorithm (snapshot pull — see DECISIONS.md ADR-008)
Data volume is tiny (KBs), so sync is a **full snapshot pull per table**, not delta sync:
1. On startup, on reconnect, on "Sync now", and on any Realtime change event for a table: pull all rows of the affected table(s) for the org, replace the SQLite table contents inside one transaction, update `sync_state.last_synced_at`.
2. Realtime subscription: one channel with `postgres_changes` on `timetables`, `periods`, `week_schedule`, `date_overrides`, `announcements`, `profiles`. The event payload is used only as a *signal*; the re-pull is the source of truth (immune to missed/out-of-order events).
3. Change events are debounced 500 ms per table (an admin saving a timetable touches many rows).

### Conflict rules
- Editing is **online-only**, so there is no offline write queue and no client-side merge — this is the single biggest simplification in the design (DECISIONS.md ADR-007).
- Concurrent admin edits: **last-write-wins** at row level, decided by Postgres commit order. `updated_at` is set by a DB trigger (server time, never client time).
- Lost-update guard: admin editor screens subscribe to `DataChanged`; if the entity being edited changed underneath, the editor shows "This timetable was just changed by <name> — reload?" before allowing save. This is a courtesy check, not a lock.
- Deletes: hard deletes, cascaded (periods with their timetable). Audit log preserves the "before" image, so nothing is silently lost. Deleting a timetable that is referenced by the week schedule or an override is **blocked** server-side (FK `RESTRICT`) and the UI explains why.

### Offline behaviour
- Cache is trusted indefinitely — a school that's offline for a week still gets correct lessons from the last-known timetable.
- UI shows "Offline — last synced <relative time>". Editing commands are disabled with a tooltip.
- Auth: the Supabase session (JWT + refresh token) is persisted encrypted with DPAPI (SECURITY.md §4). If the refresh token has expired after a long offline period, the app **keeps displaying from cache** and shows "Sign in again to sync" — display never breaks because auth expired.

---

## 7. Notification behaviour

Three categories: **lesson start**, **end warning** (N min before end, default 5), **announcement**. Each locally toggleable.

### Scheduling model
- `NotificationScheduler` does **not** use OS-scheduled toasts. It derives the next pending events from the schedule engine and fires them from the in-process 1 s tick. Rationale: the app is tray-resident by design; in-process firing lets us apply dedup, grace, and mid-day-edit rules exactly (OS-scheduled toasts can't be reliably rebuilt on edits). If the app isn't running, no notifications — acceptable because auto-start + tray make "not running" rare.
- Event key: `"{kind}:{period_id}:{yyyy-MM-dd}"` (e.g. `start:abc123:2026-07-15`) or `"announcement:{id}"`.

### Firing rules
1. On each tick: fire any pending event with `trigger_time ≤ now`, **if** `now − trigger_time ≤ 120 s` (grace window) **and** the key is not in `notification_log`.
2. After firing, insert the key into SQLite `notification_log` (pruned after 7 days). This survives restarts → no duplicate toasts after a crash/restart mid-lesson.
3. Events older than the grace window are logged as skipped and marked in `notification_log` so they never fire late.
4. On `DataChanged` or `TimeJumped`: rebuild the pending event list from scratch. Already-logged keys stay suppressed; if a period's times changed, its key is unchanged but its trigger time is new — rule: if the *old* trigger already fired and the *new* trigger time is also in the past, do not re-fire; if the new trigger is in the future, it is rescheduled and **will** fire again with the new time (edge case accepted: a moved lesson may notify twice, which is the informative behaviour).
5. End warning is suppressed when `end − start ≤ N minutes` (a 5-min break should not warn about ending the moment it starts) and when the start toast for the same period fired < 60 s ago.
6. Announcements: toast on first sighting of an unexpired announcement id not in `notification_log` — covers both Realtime delivery and discovering announcements during a reconnect pull. Announcements already expired at sighting time never toast.
7. Quiet rule: no lesson toasts on closed/no-school days (trivially true — no events exist).

### Toast content
- Start: title "Period 3 — Mathematics", body "Started now · ends 11:05". Click → activate main window.
- End warning: title "5 minutes left", body "Mathematics ends at 11:05 · next: Break".
- Announcement: title = announcement title, body = first 100 chars. Click → open announcements panel.
- AUMID registration via the Toolkit's `ToastNotificationManagerCompat` (works unpackaged); Velopack install creates the Start-menu shortcut toasts require.

---

## 8. Cross-cutting concerns

- **Time source**: machine clock. A skewed machine clock skews notifications on that machine only; server timestamps (`updated_at`, audit) always use DB time. During initial sync the app compares local time to the Supabase response `Date` header and shows a one-time warning if skew > 3 min.
- **Errors**: global exception handler → Serilog + friendly dialog; sync errors are non-fatal and surface only as the offline indicator; notification failures log and continue.
- **Performance**: recompute-on-tick is O(periods of today) ≤ ~20 comparisons/sec — no caching needed. UI virtualisation unnecessary at this data size.
- **Multi-monitor / DPI**: PerMonitorV2 DPI awareness; saved window position validated against current screen bounds on restore (monitor unplugged case → recentre).

---

## 9. Testing strategy

| Layer | Approach | Coverage focus |
|---|---|---|
| `Domain` + `Application` (schedule engine, notification event derivation, sync rules) | xUnit, pure unit tests with fake `IClock` (in `Domain.Tests` / `Application.Tests`) | **Highest value.** Every edge case in §4 and §7 gets a named test: overlaps, DST spring/fall dates, midnight boundaries, mid-day edits, grace window, dedup after restart, override precedence, next-lesson scan across closed days |
| `Infrastructure` (SQLite cache) | xUnit against real SQLite in-memory/temp file (in `IntegrationTests`) | Migrations, snapshot replace transactionality, notification_log pruning |
| `Infrastructure` (Supabase) | Integration tests against local `supabase start` (CLI) stack, run in CI on Linux | RLS behaviour per role (admin can write, staff cannot, cross-org denied), audit triggers produce rows, cascade/restrict deletes |
| ViewModels | xUnit with substituted repositories/services | Command enablement by role & connectivity, display formatting, editor dirty/conflict prompts |
| End-to-end UI | **Manual test checklist** in repo (`docs/` addition at implementation time) | Toasts, tray, startup, compact mode, sleep/resume, offline pull-the-cable — automating these on Windows CI is poor ROI for MVP (DECISIONS.md ADR-011) |

CI (GitHub Actions): build + unit tests on `windows-latest`; Supabase integration tests on `ubuntu-latest` with Supabase CLI. RLS tests are release-blocking.

---

## 10. Packaging, installation and updates

**Velopack** (successor to Squirrel) — firm choice, see DECISIONS.md ADR-010:
- Per-user install (no admin rights), installs to `%LOCALAPPDATA%`, creates Start-menu shortcut (needed for toasts).
- Auto-update: check on startup + every 6 h; download in background; apply on next restart. Silent for staff — a small "Update ready — restarts on next launch" note in Settings/About.
- Release hosting: GitHub Releases (default, B-7). Channel: single `stable` channel in MVP.
- App data locations: settings JSON + SQLite + logs in `%LOCALAPPDATA%\AqiClock\` — survives updates, removed only by explicit uninstall cleanup prompt.
- Code signing: unsigned for the pilot (SmartScreen warning on first install, documented for IT); purchase an OV/EV cert before wide rollout — flagged as pre-rollout task in TASKS.md.
- MSIX was rejected: store/signing friction, awkward HKCU Run-key startup, and Velopack's update UX is better for a small IT-light org.
