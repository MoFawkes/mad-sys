# AQI Clock — Product Specification

Status: Draft 1.0 (planning) · Last updated: 2026-07-15

AQI Clock is a Windows desktop application that shows staff the current time, the current lesson, the time remaining in it, and the next lesson, driven by a centrally managed timetable. Administrators edit timetables and post announcements; all staff machines update automatically in near-real-time and keep working offline.

---

## 1. Goals and non-goals

### Goals (MVP)
- Always-visible clock and lesson status on staff Windows machines.
- One central, admin-managed source of truth (Supabase) for timetables and announcements.
- Works offline from a local cache; resynchronises automatically.
- Native Windows notifications at lesson boundaries and for announcements.
- Zero routine maintenance on staff machines (auto-start, auto-update, tray resident).

### Non-goals (MVP)
- No per-teacher personal timetables (everyone sees the organisation timetable).
- No mobile/web client.
- No multi-organisation tenancy UI (schema allows it later; app assumes one organisation).
- No offline editing (editing requires connectivity; viewing does not).
- No localisation (English only).

---

## 2. User roles

| Role | Description | Capabilities |
|---|---|---|
| **Admin** | Office/leadership staff who own the timetable | Everything Staff can do, plus: edit timetables/periods, assign week schedule, create date overrides, post/expire announcements, view audit history, manage users' roles |
| **Staff** | Teachers and other staff | Sign in, view clock/lesson/countdown, view timetables and announcements, receive notifications, adjust personal display/notification settings (local only) |

Rules:
- Role is stored server-side (in `profiles.role`) and enforced by Row Level Security — never trusted from the client.
- Every signed-in user is Staff by default; an existing Admin promotes users to Admin.
- The first Admin is created manually in the Supabase dashboard during setup (bootstrap step, documented in TASKS.md).
- Account creation is **admin-invite only** (no open self-signup). See DECISIONS.md ADR-012.

---

## 3. MVP feature list

### 3.1 Clock and lesson display
- Large digital clock (system local time, updates every second).
- Current lesson name and time remaining (mm:ss under 1 hour, h:mm above).
- Progress bar for the current lesson.
- Next lesson name and start time. Outside school hours: "No lesson — next: <name> at <time> (<day>)".
- Today's full period list, with the current period highlighted.

### 3.2 Window modes
- **Normal mode**: clock + current/next lesson + today's list.
- **Compact mode**: small, frameless, draggable strip showing clock, current lesson, remaining time. Toggle via button, tray menu, or double-click.
- **Always-on-top**: toggle, independent of mode, persisted locally.
- Window position/size/mode persisted per machine and restored on start.

### 3.3 System tray
- Tray icon always present while running. Closing the main window minimises to tray (configurable).
- Tray tooltip: current lesson + remaining time.
- Tray menu: Open, Compact mode, Always on top, Sync now, Announcements, Settings, Sign out, Exit.
- Exit fully quits; Open restores the window.

### 3.4 Windows startup
- "Start with Windows" setting (default on), implemented via `HKCU\...\Run` registry value — no admin rights needed.
- When auto-started, launches minimised to tray.

### 3.5 Timetables
- A **timetable** is a named single-day template of ordered **periods** (e.g. "Normal Day", "Friday", "Exam Day", "Ramadan Day"). Each period has a name, start time, and end time (local wall-clock, minute precision).
- Periods may include breaks/assembly/salah — anything with a name and times. There is no distinction between "lesson" and "break" beyond an `is_lesson` display flag.
- A **week schedule** assigns one timetable (or "no school") to each weekday.
- A **date override** assigns a specific calendar date a specific timetable or "closed" (holidays, exam days, special events), taking precedence over the week schedule.
- Effective timetable resolution for a date: date override → week schedule for that weekday → no school.

### 3.6 Admin editing
- Admins edit timetables, periods, the week schedule, and date overrides in-app.
- Editing requires an active connection (writes go straight to Supabase; no offline edit queue — see DECISIONS.md ADR-007).
- Validation on save: end > start; warn (not block) on overlapping periods within a timetable; block duplicate period names within a timetable.
- Edits propagate to all clients via Supabase Realtime within seconds; each client recomputes its display and reschedules notifications immediately.

### 3.7 Staff read-only mode
- Staff see the same viewing UI with all editing entry points hidden (not merely disabled). Server-side RLS enforces read-only regardless of UI.

### 3.8 Announcements
- Admins post announcements: title, body (plain text, ≤ 2000 chars), optional expiry timestamp.
- All clients receive them in near-real-time and show a native toast + an in-app announcements panel with unread badge.
- Expired announcements disappear from the panel. Admins can delete announcements early.
- Read/unread state is local per machine (not synced) in MVP.

### 3.9 Notifications (native Windows toasts)
- **Lesson start**: toast when a period begins ("Period 3 — Mathematics has started, ends 11:05").
- **End warning**: toast N minutes before a period ends (default 5, configurable 0–15 locally; 0 = off).
- **Announcement**: toast on receipt of a new announcement.
- Each of the three categories can be toggled locally in Settings.
- Missed-notification rule: on resume/restart, a notification whose trigger time passed within the last 120 seconds still fires once; older ones are dropped silently. Notifications never fire twice for the same event (deduplicated by event key in the local cache).
- Full behaviour, timing, and edge cases: see ARCHITECTURE.md §7.

### 3.10 Offline operation and sync
- All read data (timetables, periods, week schedule, overrides, announcements, own profile) is cached in local SQLite.
- With no connectivity: clock, lesson display, and notifications work fully from cache; a subtle "offline — last synced <time>" indicator is shown; editing and sign-in are unavailable.
- On reconnect: automatic full resynchronisation, then live Realtime subscription. Manual "Sync now" available in tray/settings.
- Sync model, conflict rules, and edge cases: see ARCHITECTURE.md §6.

### 3.11 Audit history
- Every write to timetable data and announcements is recorded server-side (who, what, when, before/after) via database triggers — clients cannot bypass or forge it.
- MVP includes capture plus a minimal read-only "Recent changes" list for Admins (last 100 entries: time, user, action, entity). Rich filtering/diff view is post-MVP.

### 3.12 Settings (local, per machine)
- Start with Windows; start minimised; close-to-tray; always-on-top; compact mode; end-warning minutes; notification category toggles; theme (light/dark/system).
- Stored in a local JSON settings file (not in SQLite, not synced).

---

## 4. Post-MVP features (explicitly deferred)

| Feature | Notes |
|---|---|
| Per-user/per-class personal timetables | Biggest schema addition; org timetable model is a strict subset so it layers on cleanly |
| Multi-organisation support | Schema already keyed by `org_id`; deferred: org switching UI, org signup, billing |
| Rich audit viewer (filters, diffs, export) | Capture is in MVP; viewer is minimal |
| Announcement targeting, scheduling, acknowledgement tracking | MVP is broadcast-to-all, immediate |
| Week/month timetable view, printable view, ICS export | |
| In-app user management UI (invite, deactivate) | MVP: invites via Supabase dashboard; role changes via minimal admin screen |
| Synced per-user preferences | MVP preferences are per machine |
| Localisation (Arabic/RTL likely relevant for AQI) | Flag early in UI text handling; do not hard-code concatenated strings |
| Sound/custom notification tones | |
| Kiosk/display-board mode (full-screen wall clock) | Compact mode covers the near-term need |

---

## 5. Business decisions requiring owner input

These do not block architecture; defaults are chosen so implementation can start. Owner should confirm before launch.

| # | Question | Default assumed |
|---|---|---|
| B-1 | Organisation timezone | `Europe/London` (stored per-organisation, changeable) |
| B-2 | School week (which weekdays have lessons) | Mon–Fri (configurable via week schedule anyway) |
| B-3 | End-warning default minutes | 5 |
| B-4 | Should staff accounts be personal emails or shared device accounts? | Personal email per staff member |
| B-5 | Audit retention period | Keep forever in MVP (data volume is trivial) |
| B-6 | Supabase tier | Free tier for pilot; Pro before whole-staff rollout (Realtime connection limits) |
| B-7 | Update channel hosting | GitHub Releases (repo may need to be public or use a token); alternative: Supabase Storage |
| B-8 | App display name / icon / branding | "AQI Clock", placeholder icon |

---

## 6. Constraints and assumptions

- Windows 10 1809+ and Windows 11 (toast API and WPF/.NET 8 requirement).
- One organisation, expected scale: ≤ 200 concurrent clients, ≤ 10 timetables, ≤ 20 periods/timetable, ≤ 365 overrides/year. All data comfortably fits in memory; sync can be whole-snapshot.
- Staff machines may sleep, hibernate, restart, and lose connectivity at any time.
- The machine clock is trusted for display and notification timing (see ARCHITECTURE.md §8 for the clock-skew note).
