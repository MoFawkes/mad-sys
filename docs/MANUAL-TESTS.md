# AQI Clock manual OS-integration checklist

This checklist is the ADR-011 acceptance script for Windows surfaces that are not reliably covered by unit tests. Run the full checklist on Windows 10 and Windows 11 before the Phase 8 pilot. Record the app commit, Windows version, tester, date, and result for each run.

## Test record

- App commit: `feature/audience-aware-app` @ `5f9ffc4` plus in-session UI fixes (committed immediately after this run)
- Windows version/build: Windows 11 Home 10.0.26200
- Tester and date: Owner (MK), guided live by Fable 5 — 2026-07-23 evening
- Supabase target: local
- Result: audience-aware functional sections largely pass; **Light/Dark presentation FAILED**; sync-restart defect found — see the dated results block at the end of the audience-aware section
- Notes or issue links: PR #1; findings recorded in the 2026-07-23 session-results block below

## Prerequisites

1. Configure `AQICLOCK_Supabase__Url` and `AQICLOCK_Supabase__AnonKey`; never use a service-role key in the client environment.
2. Sign in, complete one sync, and verify the main clock is rendering cached timetable data.
3. Permit AQI Clock notifications in Windows Settings. An unpackaged development build may have limited activation identity; repeat toast activation after the Phase 8 Velopack Start-menu shortcut is installed.
4. Disable Do Not Disturb/Focus Assist when validating banner presentation. With it enabled, Windows may suppress banners while still placing successful notifications in Notification Center.

## Tray and lifecycle

- [ ] While signed in, one AQI Clock tray icon is visible and its tooltip shows the current lesson/countdown or the next lesson.
- [ ] The tooltip changes as the schedule changes, without duplicate icons or visible flicker.
- [ ] Double-click and **Open** activate the main window.
- [ ] **Compact mode** and **Always on top** show correct check marks and change the main window.
- [ ] **Announcements (n)** opens the main window and announcements panel with the current unread count.
- [ ] **Sync now** is disabled offline and starts a sync online.
- [ ] **Settings** opens Settings; **Sign out** removes the tray icon, wipes session/cache state, and shows sign-in.
- [ ] Closing the signed-in main window with **Close to tray** enabled hides it while the tray remains usable.
- [ ] **Exit** removes the tray icon and terminates the process. A later manual launch starts normally.
- [ ] Closing the sign-in window while signed out still terminates the process and leaves no hidden mutex-owning process.

## Toasts and activation

- [ ] Settings → **Send test notification** produces one native AQI Clock toast.
- [ ] Clicking the test toast activates the main window.
- [ ] A near-future period start produces exactly one `Period n — name` toast with the correct end time.
- [ ] An enabled end warning produces exactly one toast with the configured minutes, current period, end time, and next period.
- [ ] A newly synced, unexpired announcement produces one toast; its body is limited to 100 characters and clicking it opens Announcements.
- [ ] Re-syncing or restarting does not repeat already recorded notifications.
- [ ] Disabling each notification category suppresses that category without affecting the others.

## Startup

- [ ] Enabling **Start with Windows** creates `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AqiClock` containing the quoted executable path and `--minimized`.
- [ ] Disabling the setting removes only the `AqiClock` value.
- [ ] After sign-in, launching with `--minimized` starts tray-only and **Open** restores the main window.
- [ ] Reboot with auto-start enabled: AQI Clock starts once, restores the session/cache, and remains tray-only when start-minimised is enabled.

## Clock discontinuities

- [ ] Sleep through a lesson boundary for more than 120 seconds, resume, and confirm the missed notification is silently marked skipped rather than fired late.
- [ ] Sleep through a boundary for no more than 120 seconds, resume, and confirm it fires once.
- [ ] Move a future lesson after its old boundary fired; confirm the new future boundary can fire once. Removing a future boundary must stay silent.
- [ ] Simulate the local dates immediately before and after each UK DST transition. Confirm event keys use the local calendar date and each boundary fires at most once.
- [ ] Leave the app running across midnight into a scheduled day and confirm the new day's boundaries are loaded without restarting.

## Velopack install, update and uninstall

- [ ] Install `AqiClock.App-stable-Setup.exe` as a standard user; confirm no elevation is requested.
- [ ] Confirm the Start-menu shortcut exists, launches one instance, and carries a consistent AQI Clock toast identity.
- [ ] Confirm Settings → About shows the release tag version and `Up to date` after a successful check.
- [ ] Enable **Start with Windows** and verify the Run value points to the root-level stable `AqiClock.App.exe` stub, not `current\AqiClock.App.exe`.
- [ ] Reboot and confirm that stable Run path launches the updated current version once.
- [ ] Send a test toast from the packaged install; confirm banner/Notification Center attribution and click-through both say AQI Clock.
- [ ] Publish the next patch version, allow the client to download it, and confirm About says `Update downloaded — restarts into vX.Y.Z`.
- [ ] Exit and relaunch; confirm the new version is active while SQLite cache, session, settings, notification dedup state, and window placement survive.
- [ ] Confirm the executable, installer, Start-menu shortcut, window, tray, and Windows notifications use the quill-and-inkwell `assets/app.ico`.
- [ ] Uninstall through Windows Installed apps; confirm app files, Start-menu shortcut, and AQI Clock Run value are removed.
- [ ] Confirm only `%LOCALAPPDATA%\AqiClock\logs` remains when retaining diagnostic logs; manually remove other residue if the uninstall policy requests it.
- [ ] Record the expected unsigned-pilot SmartScreen warning. Repeat after signing is introduced before wide rollout.

## Password recovery and protocol activation

- [ ] Confirm `HKCU\Software\Classes\aqiclock\shell\open\command` targets the root-level stable Velopack stub and quotes `%1`.
- [ ] In Supabase Auth URL configuration, allow exactly `aqiclock://reset-password` before requesting a recovery email.
- [ ] With AQI Clock closed, request recovery, click the email link, and confirm the resizable **Set a new password** window opens.
- [ ] With AQI Clock already open at sign-in, click a second recovery link and confirm the existing instance receives it without creating a second resident process.
- [ ] Confirm a short password and mismatched confirmation are blocked locally.
- [ ] Confirm an expired/already-used link produces a friendly error without closing the recovery window.
- [ ] Complete recovery, sign in with the new password, and confirm the old password no longer works.
- [ ] Confirm logs, `settings.json`, `session.bin`, and `cache.db` contain no recovery URI, access token, refresh token, or password.
- [ ] Update AQI Clock and confirm the protocol command still targets the stable stub; uninstall and confirm the `aqiclock` protocol key is removed.

## Fluent presentation and DPI

- [ ] At 100% scaling, inspect sign-in, password recovery, Settings, every Admin tab, Main Normal, Main Compact, and Announcements in Light, Dark, and System themes.
- [ ] Confirm theme changes retain Fluent control styles, the navy accent, semantic error/warning colors, and readable contrast; no control reverts to default WPF styling.
- [ ] On Windows 11, confirm Fluent windows use Mica and rounded Fluent chrome; on Windows 10, confirm the fallback background/chrome remains readable and functional.
- [ ] At 150% scaling, repeat sign-in, Settings, Admin, and both Main modes. Confirm text is not clipped, scrolling remains available, and compact mode stays exactly 320×80 device-independent units.
- [ ] Move each open window between monitors with different scaling and confirm PerMonitorV2 reflows sharply without losing saved placement or producing an off-screen window.
- [ ] Confirm the six Admin tabs remain Timetables, Week schedule, Date overrides, Announcements, Recent changes, Users; selectors and grids remain editable and show no binding-error log entries.

## Audience-aware sign-in and announcements (PR #1)

Use the shared **Test record** above for this run. Test commit `d2e221c` or a later commit from PR #1 on a Windows build connected to a non-production Supabase target.

For PR #1, the Admin-tab checks below supersede the legacy six-tab assertion immediately above; the rewrite adds and renames Admin sections.

### Audience-aware prerequisites

- [x] Create at least two classes with distinct names and sort orders.
- [x] Tag at least one period with one class and leave another period untagged or tagged to the other class.
- [x] Have an active Admin account available for the Admin-window checks.
- [x] Prepare announcement content suitable for a future scheduled publication, class targeting, and an HTTPS e-Masjid link.
- [x] Complete a sync so the student picker has current classes and period tags in its local cache.

### Sign-in fork

- [x] Cold start opens **Choose how to continue** (`RoleChoiceWindow`) instead of opening email/password sign-in directly.
- [x] **Teacher** opens the existing email/password `SignInWindow`, and valid teacher credentials reach the main clock with the existing teacher behavior unchanged.
- [x] From `RoleChoiceWindow`, choose **Teacher**, then close `SignInWindow` without signing in. The app returns to `RoleChoiceWindow` instead of exiting.
- [x] **Student** opens `StudentClassPickerWindow` without requesting a personal identity or credentials.
- [ ] **RE-CHECK fix `23e9b5f`:** sign in and reach **Synced**, sign out and
  leave the app beyond at least one heartbeat interval with no
  `A session is required` error, then sign in again and reach **Synced**;
  repeat the out/in cycle once more.

### Student classes and optional Naseehah

- [x] Select multiple classes using the checkbox rows. With no class selected, **Start student session** remains blocked and shows the inline `Select at least one class.` error.
- [x] Confirm the independent optional Naseehah checkboxes allow all four states: AM only, PM only, both, and neither.
- [x] Select a PM-running class and AM Naseehah only. Its class-tagged PM period notification still fires; an AM-audience announcement appears/notifies and a PM-audience announcement does not.
- [x] Repeat with neither Naseehah option selected. Class-tagged period notifications remain active, while AM- and PM-audience announcements are both suppressed.
- [x] (announcements verified; a class-B-tagged *period* was not exercised) With only class A selected, confirm periods and specific-class announcements tagged only to class B do not notify or appear.
- [x] Click a period or announcement toast during an active student session. It activates the running main window rather than reopening sign-in.
- [x] Restart after a student session. No selected classes, Naseehah choices, or student identity survive; the app asks how to continue again.

### Admin — Classes / Audiences

- [x] Add, edit, save, and delete an unreferenced class using the per-row controls.
- [x] (after in-session fix: Classes tab previously had no visible error element) Save two classes with the same name or **Order** value. The Admin window shows `A class already uses that name or sort order.` instead of crashing.
- [x] Target an announcement at a class, then attempt to delete that class. The Admin window shows `This class is referenced by an announcement. Reassign or delete the announcement first.` instead of exposing an exception.
- [x] (after in-session fix: grid edits previously never committed on Save) In the period-tags grid, enter one or more valid class names in the comma-separated **Classes** column and choose **Save tags**. Sync/reload and confirm the assignments persist.
- [ ] **RE-CHECK fix `23e9b5f` (failed 2026-07-23):** enter an unknown class name while saving period tags and confirm both the Classes/Audiences banner and widened row report a useful error without losing other saved tags; then save valid tags and confirm both errors clear.
- [ ] In **Profiles / Audiences**, confirm Teacher and Admin profiles remain editable and Graduate remains visibly unavailable/coming soon.

### Admin — Announcements

- [x] Compose an announcement for a specific class with today's date and a future `HH:mm` publish time. It is scheduled, appears under **Scheduled & history**, and remains absent from active readers and notifications until that time.
- [x] Confirm the scheduled announcement is suppressed for a student session that selected a different class and becomes visible/notifiable for the selected target class once due.
- [x] Publish an announcement with a valid HTTPS e-Masjid link. Confirm the reader shows **Open e-Masjid** and clicking it opens the URL in the default browser.
- [x] Try a relative, malformed, or non-HTTPS e-Masjid link. Publishing is blocked with `The e-Masjid link must be a valid HTTPS URL.`
- [x] Delete an announcement that has a `PublishAt` value. It moves out of the active view into **Scheduled & history**, and its original publication date remains unchanged.
- [x] On a soft-deleted **Scheduled & history** item, confirm **Publish now** is disabled and cannot resurrect the announcement.
- [x] Confirm **Graduates** is absent from the Audience picker. This is intentional while Graduate sign-in and delivery are deferred; do not offer this audience until a Graduate device role can receive it.

The AM/PM and class-overlap scheduler scenarios are also covered by the automated application tests. Record the PR CI run in **Notes or issue links**; do not substitute CI for the interaction checks above.

### Audience-aware Light/Dark presentation

- [ ] **RE-CHECK fix `23e9b5f`:** inspect `RoleChoiceWindow` and `StudentClassPickerWindow` in Light, Dark, and System modes. Switching themes updates each open/new window without restarting.
- [ ] In Light mode, confirm headers use navy `#112549`, primary actions use blue `#2E6DD8`, the background uses cream `#F4F0E6`, and secondary text uses grey `#6B7280`.
- [ ] In Dark mode, confirm navy `WindowBrush` (`#112549`) surfaces and cream `HeaderBrush`/text (`#F4F0E6`) remain readable against the window/card surfaces.
- [ ] Inspect Main, Admin, Announcements, Role Choice, and Student Class Picker in both Light and Dark modes. Confirm there is no default-white chrome, clipped text, unreadable selection state, or binding-error log entry.
- [ ] **RE-CHECK fix `23e9b5f`:** re-check the Admin `DataGrid` background and main-window frame border in both themes; the normal-window native border should match `WindowBrush`, while Compact remains frameless 320×80.

`HighlightBrush` is defined as gold in both theme dictionaries but is not currently consumed by a control. Its absence on these screens is therefore not a visual failure.

### 2026-07-23 late-evening re-check (after fix commits `23e9b5f`/`2168066` + live session fixes)

**Passed this round:** sync sign-out/sign-in cycles ×2 with quiet logs;
Dark Admin grids and announcements flyout; Dark and Light Role Choice /
Student Picker palette after the live full-bleed + `ui:TitleBar` fix;
System theme; unknown-class error visible in row AND banner, clearing on
success; Graduate placeholder row disabled after the live `IsEditable`
binding fix; "Scheduled & history" label with a scheduled announcement
correctly absent from Active; student-session reader filtering (everyone /
class-A / e-Masjid items all correct); restart wipe; Compact 320×80
round-trip; main-window first-show border after the live DWM
caption/border/backdrop fixes.

**Still open — block the v0.10.0 tag:**

1. **Main-window border after a THEME CHANGE** — first show is clean, but
   switching themes leaves the mismatched titlebar/border despite color
   re-application (external `DwmSetWindowAttribute` renders instantly, so
   WPF-UI's theme pass re-applies a backdrop or resets colors after all
   our hooks; `WindowBackdrop.RemoveBackdrop` + retries were not enough).
2. **Class-B period toast NOT delivered to a Class-B student session**
   (21:50 boundary): `notification_log` has no entry for the boundary at
   all, while an earlier session (19:20) delivered correctly on the
   pre-fix build. Suspect: the scheduler's day plan is not rebuilt when a
   student session starts after launch (possibly interacting with
   `MainViewModel.InitializeAsync` now running at student start), or
   boundary dedup after repeated same-day retimes. Needs a scoped repro.
3. **Teacher sign-in showed admin controls** while
   `profiles.role='teacher'` is confirmed server-side — client
   role-resolution bug (server RLS still denies writes). Reproduce with
   confirmed account identity first.
4. **No tray icon during student sessions** — students have no Exit path
   (tray only appears for signed-in users). Decide: show tray for student
   sessions or provide another exit.
5. **Current-lesson card mixed state** (screenshot in session notes):
   card showed "Break / ends 21:42" while the cache correctly held
   Registration until 21:42 — likely engine day-snapshot staleness under
   rapid same-day timetable changes; verify under a normal timetable
   change before treating as a blocker.

**Unverified (retime windows missed):** class-B-tagged period suppression
for a Class-A session (the announcement-side equivalent passed); the
teacher regression pass is blocked by item 3.

**Fix `a4bdfee` — owner re-check remains open:**

- [ ] Switch Light → Dark → Light without a Compact round-trip and confirm
  the normal titlebar/border matches `WindowBrush` after both changes;
  Compact remains frameless 320×80.
- [ ] Sign in with a confirmed Teacher account when its cached profile
  incorrectly says Admin and confirm admin controls never appear. Then sign
  in as a genuine Admin and confirm controls appear only after fresh profile
  sync. Offline cached Admin stays teacher-level by design.
- [ ] Start a Class-B student session from a signed-out cold start and confirm
  the Class-B boundary toast fires. Confirm a Class-A session suppresses the
  same boundary. The fix rebuilds on `AudienceChanged` and also clears a
  persisted stable-key dedup entry only when its trigger actually moved.
- [ ] In a student session, confirm the tray contains exactly **Open**,
  **Announcements (n)**, **End student session**, and **Exit**; exercise all
  four actions.
- [ ] During one normal teacher-session timetable edit, confirm the lesson
  card updates consistently. Automated coverage confirms name/end/remaining
  are recomputed together after `DataChanged`; initial load can wait up to
  the next one-second clock tick.

The sync-cycle diagnostic is now Information-level because the application
logging filter defaults to Information; quiet Offline states therefore remain
present in the rolling file log without restoring per-heartbeat error spam.

### 2026-07-23 session results (owner click-through, local stack)

**This section FAILED.** The five Light/Dark items above remain unticked because:

- The Navy/Cream palette does not render on `RoleChoiceWindow` or
  `StudentClassPickerWindow` in either theme — both show default grey
  WPF-UI surfaces. The XAML correctly binds `WindowBrush`/`HeaderBrush`;
  WPF-UI's `FluentWindow` background management overrides
  `Window.Background` after load (the same mechanism the v0.9.6
  main-window fix addressed for the plain window).
- The main window shows a black frame ring again in Dark mode.
- The Admin Dark grid re-check and part of the theme matrix were blocked
  by the sync defect below ("not synced" after sign-out/sign-in).

Fix `23e9b5f` moves the palette brush to each Fluent window's inner grid,
matches the normal main window's DWM border to `WindowBrush`, and preserves
Compact's frameless path. The owner boxes intentionally remain unticked for
the guided visual re-check.

**Merge-blocking defects found this session:**

1. Sign-out leaves `SyncService` running (heartbeat logs
   `A session is required` indefinitely) and a later sign-in's
   `sync.StartAsync` is a no-op because of the `_lifetime` start-once
   guard — the session ends up permanently "not synced". The student
   flow makes sign-out/sign-in cycles routine, so this must be fixed.
2. The palette/frame failures above.
3. The period-tags inline "Unknown: ..." error never renders visibly
   (marked FAIL above) — the message should also surface in the
   Classes-tab error banner.

Fix `23e9b5f` addresses the three blockers: sync teardown/restart and quiet
signed-out ticks; inner-grid palette rendering plus native frame treatment;
and the period-tag banner/widened row. It also initializes the main cached
display on student entry and fixes scheduled expiry relative to publication.
These statements record implementation and automated coverage, not owner
acceptance; the corresponding `[ ]` re-check boxes remain open.

**Fixed live during the session (verified by re-test, committed on the branch):**

- Classes/period-tags `DataGridTextColumn` edits never committed on Save
  (`UpdateSourceTrigger=PropertyChanged` added) — previously every save
  wrote the stale defaults.
- The Classes tab had no visible element for its error property; the
  duplicate-name message now renders.

**Observations for the owner to ratify (not defects until decided):**

- Scheduled announcements remain in the combined archive until due and
  "Publish now" still publishes immediately; the approved label is now
  **Scheduled & history**.
- Untagged periods (e.g. Break) do not notify class-filtered student
  sessions. Plausibly intended; document it if so.
- A signed-out student device cannot pull announcements created after
  its last signed-in sync (no session for REST/Realtime). The scheduled
  due-time flip works from cache, but genuinely new server content will
  not arrive mid-student-session. Decide whether that is acceptable for
  this release.
- Cold-start close of the sign-in window exited the whole app **once**
  (first run of the evening); it could not be reproduced afterwards —
  the cancel path now reliably returns to the role choice. Watch for
  recurrence.
- One unhandled `VirtualizingStackPanel` layout exception was logged
  while the (pre-fix) classes grid was being fought; likely tied to the
  reload-during-edit behaviour. Re-check after the fixes.

**Still untested:** Profiles/Audiences tab (Teacher/Admin editable,
Graduate visibly unavailable); a class-B-tagged *period* suppression
case; teacher-account regression pass; tray **Exit** discoverability
from a student session.

### Release decision

Decided 2026-07-23: v0.9.6 shipped the earlier theme fixes on their own, and
the audience-aware work in PR #1 will ship as v0.10.0. After architect diff
verification, the open re-check boxes plus the four still-untested items are
the remaining owner acceptance before tagging; the production-like migration
rehearsal runs automatically in CI.
