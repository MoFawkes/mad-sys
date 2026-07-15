# AQI Clock — Screens and User Journeys

Status: Draft 1.0 (planning) · Last updated: 2026-07-15

Roles: **Staff** (read-only) and **Admin** (Staff + editing). Admin-only elements are hidden — not disabled — for Staff.

---

## 1. Screen inventory

| # | Screen | Access | Purpose |
|---|---|---|---|
| S1 | Sign-in window | Signed-out | Email + password |
| S2 | Main window — Normal mode | All | Clock, current/next lesson, today's list |
| S3 | Main window — Compact mode | All | Minimal always-on-top strip |
| S4 | Announcements panel | All (compose: Admin) | Read announcements; Admin: post/delete |
| S5 | Settings window | All | Local per-machine preferences |
| S6 | Admin window — Timetables tab | Admin | Create/edit/archive timetables and periods |
| S7 | Admin window — Week schedule tab | Admin | Assign timetable per weekday |
| S8 | Admin window — Date overrides tab | Admin | Calendar-date exceptions |
| S9 | Admin window — Recent changes tab | Admin | Read-only audit list (last 100) |
| S10 | Admin window — Users tab | Admin | List users, change role, deactivate |
| S11 | Tray icon + menu | All | Resident control surface |
| S12 | About dialog | All | Version, update status, log location |

---

## 2. Screen details

### S1 — Sign-in
Email, password, "Sign in" button, error line. No self-registration link (invite-only). "Forgot password" sends Supabase reset email.
States: idle / signing-in / error ("Incorrect email or password", "No connection — sign-in requires internet").
After success: initial sync with progress ("Downloading timetable…"), then main window. If a previous session's cache exists for the same user, main window opens immediately and sync runs in background.

### S2 — Main window (Normal)
Top→bottom: large clock (HH:mm:ss) · date · **current lesson card** (name, "ends 11:05", remaining countdown, progress bar) or "No lesson right now / No lessons today" · **next lesson line** ("Next: Break at 11:05") · today's period list (current highlighted, past dimmed) · status strip (sync state: "Synced · just now" / "Offline — last synced 2 h ago" / "Sign in again to sync"; announcements bell with unread badge).
Title-bar/buttons: compact-mode toggle, always-on-top pin, settings gear; Admin additionally sees "Edit timetables" button.
Close button behaviour: minimise to tray (default, configurable to real close).

### S3 — Main window (Compact)
Frameless, draggable, ~320×80: clock · current lesson name · remaining time; thin progress bar as the bottom edge. Hover reveals: expand, pin, close-to-tray buttons. Double-click toggles back to Normal. Remembers its own position separately from Normal mode.

### S4 — Announcements panel
Slide-in panel over the main window (not a separate window). Newest first: title, body, relative time, poster name; unread shown bold, marked read when panel is opened. Expired items absent.
Admin extras: compose (title, body, optional expiry picker: end of day / end of week / custom / never), delete button per item with confirm.

### S5 — Settings
Sections: **General** (start with Windows ✓, start minimised ✓, close to tray ✓, theme), **Display** (always on top, compact on launch), **Notifications** (lesson start ✓, end warning ✓ + minutes slider 0–15 def 5, announcements ✓, "Send test notification" button), **Account** (signed-in email, role badge, Sync now, Sign out), **About** (version, update state, view logs).

### S6 — Timetables tab
Left: timetable list (+ New, context: rename / duplicate / archive). Right: selected timetable's period grid — Name, Start, End, Lesson? checkbox, drag to reorder; add/delete row; Save/Cancel with dirty tracking.
Validation on save: end > start (block), duplicate names (block), overlaps (⚠ warning banner, allowed).
Concurrency: if the same timetable changes remotely while dirty → banner "Changed by <name> just now — Reload / Overwrite".
Delete/archive rules: archive always allowed; delete blocked with explanation if referenced by week schedule or an override ("Used by: Monday, 12 Sep — reassign first").

### S7 — Week schedule tab
Seven rows Mon–Sun, each a dropdown: any active timetable or "No school". Saves per-row immediately (small blast radius, no dirty state).

### S8 — Date overrides tab
Upcoming overrides list + "Add override": date picker, timetable dropdown (incl. "Closed"), note. Editing/deleting past overrides allowed (corrections) but list defaults to today-onward. Duplicate date → replaces after confirm.

### S9 — Recent changes tab
Read-only table: When · Who · Action · What (e.g. "Updated period 'Maths' in 'Normal Day'"). Online-only; shows "Connect to view history" when offline. No filters in MVP.

### S10 — Users tab
Table: name, email, role, active. Actions: toggle Staff/Admin (confirm; cannot demote yourself if you are the last active admin — checked server-side), deactivate/reactivate. "Invite" is out of scope: an info box explains invitations are done in the Supabase dashboard (MVP).

### S11 — Tray
Tooltip: "Mathematics — 12:34 left" / "No lesson · next 13:30". Menu: Open · Compact mode ✓ · Always on top ✓ · Announcements (n) · Sync now · Settings · Sign out · Exit. Double-click = Open. Announcement toast click opens S4.

---

## 3. User journeys

### J1 — First run (staff member)
Installer run → app starts → S1 sign-in → initial sync progress → S2 Normal mode → toast permission is implicit (Windows) → close button minimises to tray → auto-start registered. *Total: under two minutes, no configuration required.*

### J2 — Daily passive use (staff)
Machine boots → app auto-starts minimised → tray tooltip live → 08:25 toast "Period 1 — Registration has started" → user opens compact mode, pins it above their slides → 5-min warning toasts through the day → machine sleeps at lunch, resumes: display correct within a second, missed mid-sleep notifications silently skipped (grace rule).

### J3 — Admin edits during the school day
Admin opens Admin window → Timetables → shortens Period 5, saves → all online clients re-pull within ~2 s, countdowns and pending notifications rebuild; offline clients pick it up on reconnect. Audit rows appear in S9.

### J4 — Planning an exam week
Admin duplicates "Normal Day" → "Exam Day", edits periods → Date overrides: adds Mon–Fri next week = "Exam Day" → clients show normal timetable today and switch automatically on the override dates at midnight (next-lesson lookahead already reflects it).

### J5 — Announcement
Admin composes "Staff meeting moved to 15:30", expiry end-of-day → every online client toasts within seconds; offline clients toast on reconnect (if not yet expired) → badge clears per user when panel opened → announcement vanishes everywhere after expiry.

### J6 — Offline day
Internet down at 07:00 → app starts, cache present → full display + notifications work; status shows "Offline — last synced yesterday 16:12" → editing hidden/disabled → 11:00 internet returns → auto re-sync, status returns to "Synced", any new announcements toast.

### J7 — Role change
Admin promotes a teacher in S10 → target's client receives profile change via Realtime → "Edit timetables" button appears live (no re-login needed; JWT role is not used for UI — `profiles.role` from cache is).

### J8 — Sign out / shared machine
Tray → Sign out → confirm → cache and saved session wiped (SECURITY.md §4) → S1 shown.

### J9 — Update
Velopack finds an update at 06:00 check → downloads silently → next app launch runs new version → About shows new version number.

---

## 4. UI edge-case behaviour (normative)

| Situation | Behaviour |
|---|---|
| No timetable assigned today | "No lessons today" + next school day's first lesson |
| Between periods | "Break / no lesson" state, next lesson emphasised, no countdown bar |
| Overlapping periods | Display follows engine rule (latest start wins); today's list shows both with a subtle ⚠ for admins only |
| Period deleted while it is current | Display flips to between-periods state on next tick |
| Laptop resume mid-period | Correct within one tick; `TimeJumped` rebuilds notifications |
| Cache empty + offline (first run without internet) | Explanatory empty state: "AQI Clock needs internet for first-time setup" |
| Last-synced > 7 days | Status strip escalates to warning colour: "Timetable may be out of date" |
| Windows Focus Assist / notifications muted | Toasts are queued by Windows per its own rules; app does not fight it; in-app display is the fallback |
