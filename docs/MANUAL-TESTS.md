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
- [ ] A near-future period start produces exactly one toast titled with the period name alone and the correct end time.
- [ ] An enabled end warning produces exactly one toast with the configured minutes, current period and end time, plus a separate capitalised `Next:` line when another period follows.
- [ ] A newly synced, unexpired announcement produces one toast; its body is limited to 100 characters and clicking it opens Announcements.
- [ ] Re-syncing or restarting does not repeat already recorded notifications.
- [ ] Disabling each notification category suppresses that category without affecting the others.

## 2026-08-11 teacher-feedback regression pass

- [ ] Keep the main window open with Today's periods scrolled, force repeated syncs, and confirm no error dialog or new UI exception appears in the log.
- [ ] Trigger more than one handled UI exception in a development build and confirm only the first shows a modal dialog while every exception is logged.
- [ ] Open Settings on a student device: the account reads `Student device`, the badge reads `Student`; signed-out state has no badge; teacher/admin displays are unchanged.
- [ ] Open the student-session picker and confirm the Do Not Disturb notice and the explanatory AM/PM Naseehah copy fit without clipping.
- [ ] Confirm Classes / Audiences contains only class CRUD, and class add/save/delete plus class-specific week-schedule rows still work.

## Startup

- [ ] Enabling **Start with Windows** creates `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AqiClock` containing the quoted executable path and `--minimized`.
- [ ] Disabling the setting removes only the `AqiClock` value.
- [ ] After sign-in, launching with `--minimized` starts tray-only and **Open** restores the main window.
- [ ] Reboot with auto-start enabled: AQI Clock starts once, restores the session/cache, and remains tray-only when start-minimised is enabled.

## Clock discontinuities

- [ ] Set the desktop and phone to a zone different from `Europe/London`; both show institute time, label the mismatch, and fire a lesson notification at the correct absolute instant.
- [ ] Repeat around the 2026-03-29 and 2026-10-25 London DST transitions; lesson wall times remain unchanged and each notification fires once.

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
- [ ] Confirm the app prompts once to restart into the downloaded version. Choose **No** and confirm the app remains open with the downloaded status; repeat with a fresh pending update, choose **Yes**, and confirm AQI Clock exits, applies the update, and relaunches automatically.
- [ ] Confirm the new version is active while SQLite cache, session, settings, notification dedup state, and window placement survive.
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

   **Concrete repro, 2026-08-07:** with the desktop app running and signed in,
   insert or change today's live timetable so a new current period arrives by
   sync. Immediately after `DataChanged` handling, **TODAY'S PERIODS** can show
   the new period while the current and next cards still read **No lessons
   today** / **No upcoming lessons**. The state self-corrects without user
   action and notifications remain correct. Suspected—but not proven—cause:
   Timetables, Periods, and WeekSchedule signals each start a fire-and-forget
   `ReloadAsync`, allowing concurrent snapshot replacements. Capture timestamps
   and reload completion order before treating that hypothesis as diagnosed.

**Unverified (retime windows missed):** class-B-tagged period suppression
for a Class-A session (the announcement-side equivalent passed); the
teacher regression pass is blocked by item 3.

**Fix `a4bdfee` — owner re-check completed 2026-07-23 ~22:20 (final pass):**

- [x] Switch Light → Dark → Light without a Compact round-trip and confirm
  the normal titlebar/border matches `WindowBrush` after both changes;
  Compact remains frameless 320×80. **PASS** — the `WindowBackdropType.None`
  application-wide change resolved it; Fluent windows keep their own Mica.
- [x] (fresh-role halves verified; the stale-cache-admin simulation was not
  separately staged — the capping logic is unit-covered by
  `SignInDoesNotElevateFromCachedAdminProfile`/restore variant) Teacher
  account never showed admin controls; genuine Admin gained controls after
  fresh profile sync. Offline cached Admin stays teacher-level by design.
- [x] (delivery + moved-boundary refire verified: the boundary key had a
  same-day entry from an earlier timing and re-fired at 22:21;
  the banner was suppressed by Windows' default post-22:00 Do Not Disturb —
  the notification was present in Notification Center, matching
  Prerequisite 4. Class-A suppression of a class-B period remains covered
  by unit test only.) Start a Class-B student session from a signed-out
  cold start and confirm the Class-B boundary toast fires.
- [x] In a student session, confirm the tray contains exactly **Open**,
  **Announcements (n)**, **End student session**, and **Exit**; exercise all
  four actions. **PASS.**
- [x] (cleared by automated coverage; the live attempt could not complete
  because the period-name cell in the *timetable editor* would not commit
  its edit — a pre-existing v0.9.x grid quirk in a screen untouched by this
  release, logged as follow-up backlog: add `UpdateSourceTrigger=
  PropertyChanged` to the timetable-editor period columns, mirroring the
  Classes-grid fix.) During one normal teacher-session timetable edit,
  confirm the lesson card updates consistently. Automated coverage confirms
  name/end/remaining are recomputed together after `DataChanged`; initial
  load can wait up to the next one-second clock tick.

**Final verdict 2026-07-23 ~22:30: the audience-aware acceptance is
COMPLETE. v0.10.0 is cleared for tag and release.**

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
- Resolved 2026-08-01: desktop student devices now use anonymous enrolment,
  REST snapshot sync, and Realtime, so new announcements arrive without a
  teacher signing in on that PC.
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

## v0.11.1 desktop admin-save checks

- [ ] Admin → Timetables → Full-Time Friday: move a middle period up and Save. Confirm the order persists after Cancel/reselect and no remote-change banner appears.
- [ ] Delete a middle period and Save; then add a period, move it up twice, and Save; then swap two period names and Save. Confirm every operation succeeds and persists.
- [ ] After deleting all `public.week_schedule` rows locally, assign Monday's timetable and Save. Restart and confirm the assignment persists. In Light and Dark themes, verify Classes / Audiences has clear Name/Order spacing and sweep all seven Admin tabs for cell-layout regressions.

## v0.11.0 desktop QoL checks

- [ ] Sign in as staff, reboot with auto-start and delayed Wi-Fi, and confirm cached signed-in state appears before sync returns to **Synced**.
- [ ] Leave Admin open for more than one hour and edit a period for at least two minutes; confirm no 30-second banner flash, tab disable, focus loss, or expired-session outage.
- [ ] Enrol a desktop student device with a join code, choose classes and AM/PM, restart, and confirm it opens on the clock with the same choices and **Synced** status. Publish a teacher announcement and confirm it arrives without a teacher session on that PC.
- [ ] At 1366×768 and 100%, 125%, and 150% scaling, open, resize, close, and reopen Admin and Settings. Confirm both remain inside the work area and restore their size.
- [ ] At each scale in Light and Dark, confirm the periods-grid up, down, and red delete buttons are fully visible and clickable.
- [ ] Choose **End student session** from the tray and confirm both the enrolment/session and saved class choices are removed.
- [ ] Edit a period on desktop machine A and confirm it reaches machine B through Realtime without restarting either app.
- [ ] Enter a wrong password and confirm the message is **Incorrect email or password**. Revoke a stored refresh token and confirm restart returns to the sign-in window rather than leaving a silent dead session.

## v0.14.0 desktop teacher-feedback checks

- [x] From role choice, open teacher sign-in and use **Back**; confirm role choice returns and the app does not exit. Repeat with Esc.
- [x] From the student class picker, use **Back** and confirm role choice returns. Repeat after the device is already enrolled as a student device.
- [ ] Sign in with a confirmed inactive Windows account and confirm the message is **Your account is inactive; contact an administrator.** rather than a connectivity error.
- [ ] Create two lesson periods starting in the same minute and confirm desktop produces one notification listing both periods.

**Desktop teacher-feedback result (2026-08-18): PARTIAL PASS.** The rebuilt
`0.13.4-dev.2+5a237c6` binary returned from teacher sign-in through both **Back**
and Esc without exiting, and picker Back returned correctly. Closing teacher
sign-in with the title-bar X still exited because WPF reached zero windows
before the `Closed` handler could reopen role choice. That X path remains a
release blocker pending rebuild and re-test. The inactive-account and combined
desktop-notification rows also remain open.

## v0.14.0 mobile acceptance checks

**Preview APK evidence (2026-08-01):** EAS build
`f2b8871d-919f-41df-96e6-104b621cbee4` was built from merged `main` commit
`357e48f` with the `preview` profile (internal Android APK), SDK 54,
version 0.11.0, and both preview Supabase client variables loaded. The merged
manifest targets Android API 36 and contains `POST_NOTIFICATIONS` and
`RECEIVE_BOOT_COMPLETED`; it contains neither `SCHEDULE_EXACT_ALARM` nor
`USE_EXACT_ALARM`. The downloaded APK SHA-256 is
`30A9D1A401060811669DED527733CF1186657F251A8CD5C3485515DEF24B9866`.
Keep the exact-alarm decision open until the two physical
drift measurements below are recorded. The Lessons channel is configured as
`HIGH` in source, but heads-up behavior and the installed channel importance
still require device verification.

- [x] **MOB-F01 — Visual alignment:** compare all seven native routes with `mobile/design/` at the target phone size. Confirm no hamburger on tab roots, no tabs in setup, Settings retains tabs, and the clock list scrolls fully clear of its status strip.
- [x] **MOB-F02 — Clock composition:** confirm NOW contains the remaining-time pill, end time, progress, and next lesson; past rows dim and the active Today row has its left marker.
- [x] **MOB-F03 — Announcements:** confirm category, relative timestamp, unread dot, two-line body clamp, and standalone eMasjid row without clipping at large text sizes.
- [x] **MOB-F04 — Class picker:** confirm classes and independent optional AM/PM choices remain visually separate and the validation state still says **Select at least one class.**
- [x] **MOB-F05 — Teacher:** sign in with an invited active account, compare the mobile and desktop current/next lesson against the same wall clock, and verify pull-to-refresh plus Settings → Sync now.
- [x] **MOB-F06 — Inactive teacher:** deactivate the account, sync, and confirm the clock shows **Your account is inactive; contact an administrator** rather than an empty timetable.
- [x] **MOB-F07 — Teacher sign-out:** confirm the session, SQLite cache, announcement/read state, and pending lesson notifications are removed.
- [x] **MOB-F08 — Student fresh install:** sign in anonymously, enter the join code, select at least one class plus AM only, and confirm the choices survive an app restart.
- [x] **MOB-F09 — Student audience:** confirm other-class periods/notifications are absent, untagged breaks/assemblies remain visible, a PM announcement is hidden, and a teachers announcement is absent from the API response.
- [ ] **MOB-F10 — Student settings:** confirm there is no role switch or teacher-only surface, **Change my classes** reopens the picker, and **End student session** clears selection/session/cache.
- [ ] **MOB-F11 — Join-code QR:** open desktop **Student devices**, confirm the grouped code and QR render, scan with the emulator virtual scene or enter it manually (record which), and confirm `/student-setup` opens prefilled without auto-submitting.
- [ ] **MOB-F12 — Join-code normalisation:** enter the same code manually with spaces and confirm enrolment succeeds. Verify lowercase and dash-separated input also work.
- [ ] **MOB-F13 — Join-code rotation:** rotate the code on desktop; the old code cannot enrol a new phone, the new code can, and an already-enrolled phone continues syncing.
- [ ] **MOB-F14 — Device revocation:** remove all student devices on desktop; the phone routes to setup with **This device is no longer enrolled. Ask for a new join code.**
- [x] **MOB-F15 — Non-admin refusal:** sign in as a non-admin teacher; the desktop tab and mobile section are absent, and a direct admin RPC call is refused.
- [x] **MOB-F16 — App identity:** inspect launcher circle/squircle masks and navy splash at device size for clipping or aliasing.
- [x] **MOB-F17 — Settings:** verify all three notification toggles persist, end-warning clamps at 0 and 15, About reports v0.14.0, and the tab label is **Announcements** with no tab-screen back arrows.
- [x] **MOB-F18 — Merged multi-class agenda:** enrol a device in two classes whose weekday routes to different timetables; confirm both classes' periods appear in one ordered list with class labels. Confirm a single-class device shows no labels.
- [x] **MOB-F19 — Shared timetable:** enrol a device in two classes routed to the same timetable; confirm every period appears once, no class label is shown, and lesson notifications remain scheduled.
- [x] **MOB-A01 — Exact alarms (emulator):** confirm `SCHEDULE_EXACT_ALARM` is allowed and `dumpsys alarm` shows exact alarms for `com.mofawkes.aqiclock`.
- [x] **MOB-A02 — Forced Doze (emulator):** force idle, observe an exact lesson notification, then leave forced idle.
- [x] **MOB-A03 — Rescheduling (emulator):** move a period on desktop, foreground the phone, and confirm old pending notifications are cancelled and replacements use the new time.
- [x] **MOB-A04 — Permission denied (emulator):** deny notification permission and confirm clock, sync, and announcements continue working.
- [x] **MOB-A05 — Combined mobile notifications:** create two periods starting in the same minute and confirm mobile produces one notification listing both periods.
- [x] **MOB-T01 — Start drift (Pixel 9 Pro):** unplug the phone, leave battery optimisation at **Optimised**, background the app with a lesson start at least 10 minutes ahead, and record scheduled time, delivered time, and drift from Notification history.
- [x] **MOB-T02 — End-warning drift (Pixel 9 Pro):** repeat for a 2-minute end warning and record scheduled time, delivered time, and drift.
- [x] **MOB-T03 — Overnight offline (Pixel 9 Pro):** leave the phone offline overnight and confirm the next day's previously scheduled notifications arrive; record delivery timestamps from Notification history.
- [x] **MOB-T04 — Three-day background (Pixel 9 Pro):** background/close the app for three days without opening it and confirm the background task keeps extending the pending notification window.
- [x] **MOB-T05 — Reboot reschedule (Pixel 9 Pro):** reboot and confirm `RECEIVE_BOOT_COMPLETED` restores pending notifications.
- [ ] **MOB-T06 — Class-switch endurance (Pixel 9 Pro):** enrol as class A and let notifications schedule, switch to class B, then verify over several days under forced Doze that no class A notification arrives.

**v0.14.0 rebuilt-APK result (2026-08-18): PARTIAL PASS.** EAS preview build
`f9f0a6a3-fc7c-4d2c-aa4d-341d50170216` passed `MOB-F07`, `MOB-F18`,
`MOB-F19`, and the mobile-only `MOB-A05`. Evening Session 1 + 2 produced one
ordered, class-labelled agenda; routing both to Part-Time 1 produced its six
rows once each without labels. Ending the student session reduced pending
lesson alarms from 60 to 0, clearing the failure recorded on 2026-08-12. Two
20:48 periods produced one exact alarm and one notification whose Android text
was `ZZ Combined B\nZZ Combined A`; A's five-minute end warning was correctly
suppressed and B's fired separately at 20:49. `MOB-F10` remains unchecked:
Settings exposed only the student controls and version 0.14.0, but the SQLite
cache wipe was not inspected. The positive overlapping-timetable warning was
observed, but `MOB-F20` was removed because real staggered class timetables make
that warning permanently noisy; the feature is deferred to the generator work.

**Pixel 9 Pro result (2026-08-12): PASS.** The owner confirmed all five
`MOB-T01`–`MOB-T05` checks passed on physical hardware, including start and
end-warning delivery, overnight-offline delivery, the three-day background
horizon, and reboot rescheduling. This entry records the owner's acceptance;
retain the device Notification history separately as the timestamp evidence
for the two drift checks.

**Emulator preflight (2026-08-12):** Android API 36 emulator
`Medium_Phone_API_36.1` booted with AQI Clock 0.14.0 installed. Package and
AppOps inspection confirmed `USE_EXACT_ALARM`, `SCHEDULE_EXACT_ALARM`, and
`RECEIVE_BOOT_COMPLETED`, with exact-alarm access allowed. With notification
permission denied, the app launched and the role-choice and join-code screens
remained usable. No signed-in teacher or enrolled-student state was available,
so this preflight does not by itself complete `MOB-A01`–`MOB-A04` or the
role-specific `MOB-F*` rows.

**Student emulator pass (2026-08-12):** PASS for `MOB-F04`, `MOB-F08`,
`MOB-A01`, and `MOB-A04`. The owner supplied the enrolled anonymous-student
session. The class picker kept classes separate from AM/PM choices and produced
the exact required validation message. Morning Year 1–5 plus AM-only persisted
across a force-stop/relaunch. `SCHEDULE_EXACT_ALARM` AppOps was allowed and
`dumpsys alarm` showed AQI Clock `RTC_WAKEUP` entries with `window=0` and
`exactAllowReason=policy_permission`. With `POST_NOTIFICATIONS` denied and
user-fixed, the clock rendered cached schedule data, a manual sync advanced the
last-sync time, and Announcements and Settings remained usable. Settings also
showed the student-only account surface and **Version 0.14.0**. Rows whose full
scenario needs teacher/admin actions, targeted announcements, an active lesson,
forced Doze, timetable editing, or destructive session cleanup remain open.

**Admin emulator pass (2026-08-12):** PASS for `MOB-F17`. The owner supplied
an active admin session. The teacher clock rendered and pull-to-refresh advanced
the sync state; Settings exposed the admin-only Student Devices section. All
three notification toggles were disabled, survived a force-stop/relaunch, and
were then restored enabled. The warning-time control stopped at 0 and 15 and
was restored to 5 minutes. About reported **Version 0.14.0**, the tab label was
**Announcements**, and tab roots had no back arrows. No join code was rotated,
no device was revoked, and the admin account/session was left intact.

**Forced-Doze/rescheduling pass (2026-08-12):** PASS for `MOB-A02` and
`MOB-A03`. The default Wednesday timetable's Lesson 8 was temporarily moved
from 13:00–13:30 to 21:38–21:45. After foreground sync, Android scheduled exact
`RTC_WAKEUP` alarms for the 21:38 start and 21:40 five-minute warning. The
emulator was backgrounded and forced into deep `IDLE`; the start notification
appeared at 21:38:00 and the end-warning was present at 21:40:10. For the next
day, the old 13:00 alarm was absent and the replacement 21:38 alarm was present.
Deep idle was released, Lesson 8 was restored to 13:00–13:30 in production,
the app synced again, and the next-day exact 13:00 alarm was verified restored.
The paired Pixel also received both temporary notifications, confirmed by the
owner on 2026-08-12.

**Active-clock pass (2026-08-12):** PASS for `MOB-F02`. Production Wednesday
Lesson 8 was temporarily moved from 13:00–13:30 to 21:40–21:50. At 21:42 the
NOW card showed **8 min left**, **Lesson 8**, **ends 21:50**, a partially filled
progress bar, and the next lesson. Past Today rows were dimmed and the active
Lesson 8 row had its left marker and full outline. Lesson 8 was then restored
to 13:00–13:30 and production was re-queried to verify the original values.

**App-identity pass (2026-08-12):** PASS for `MOB-F16`. On the Android API 36
emulator, the app drawer showed the AQI Clock cream circular icon with the navy
quill-and-inkwell mark centered, crisp, and unclipped. A cold launch showed the
same mark centered on the navy splash without clipping or visible aliasing.

**Seven-route visual pass (2026-08-12):** PASS for `MOB-F01`. The Android API
36 emulator routes were compared with the seven `mobile/design/` hierarchy
references at the target phone size. Earlier first-run/student acceptance
provided the role choice, teacher sign-in, join-code, and class-picker states;
the current admin session provided fresh Clock, Announcements, Settings, and
Clock-bottom captures. The setup flow had no tabs, tab roots had no hamburger
or back affordance, Settings retained the three-tab bar, and the Clock scrolled
through Lesson 8 with the final row fully above the fixed sync-status strip.
Differences were limited to live data/role state and the intentionally corrected
chrome documented in `mobile/design/README.md`; no clipping or hierarchy defect
was found.

**Active-teacher pass (2026-08-12):** PASS for `MOB-F05`. The dedicated active
non-admin test teacher signed into the emulator. At the same London wall-clock
time, mobile and the owner-observed Windows client both showed **No lesson
now** and **Next: Lesson 1 at 09:10**, matching the production default Wednesday
timetable. Pull-to-refresh completed, and Settings → **Sync now** advanced the
mobile last-sync timestamp to 22:18. Settings identified the account as Teacher
and did not expose the admin-only Student Devices section.

**Inactive-teacher pass (2026-08-12):** PASS for mobile `MOB-F06`. The
dedicated test teacher was temporarily deactivated in production. After mobile
sync, the Clock replaced the timetable with **Your account is inactive** and
**Contact an administrator to restore access to the timetable.** The account
was immediately restored active and the production profile was re-queried to
confirm `is_active = true`. During the same check, the Windows client returned
the inactive teacher to sign-in with **Signed in, but the initial timetable
download failed. Check your connection and try again.** That desktop-specific
misclassification is a separate release defect and is not credited as a mobile
failure.

**Non-admin refusal pass (2026-08-12):** PASS for `MOB-F15`. With the dedicated
test teacher active, mobile Settings omitted Student Devices and identified the
account as Teacher. The owner confirmed the Windows staff client exposed only
Compact, Pin, and Settings, with no Student Devices/admin surface. A direct
`admin_delete_week_schedule` call executed under that staff identity targeted
a nonexistent UUID and was rejected before mutation with PostgreSQL `42501`
and **Administrator access is required**.

**Teacher sign-out failure (2026-08-12):** FAIL for `MOB-F07`. Signing the
dedicated teacher out returned mobile to the role-choice screen, confirming the
visible session was removed. However, after the app settled, Android
`dumpsys alarm` still listed the full set of exact AQI Clock lesson start and
end-warning alarms, including Monday Lesson 1 at 09:10 and its 09:35 warning.
The installed release APK is not debuggable, so the internal SQLite tables
could not be independently inspected. Because pending lesson notifications
were demonstrably retained, the checklist row remains open and release is
blocked until the cleanup path is fixed and re-tested.

**Source fix pending rebuild (2026-08-12):** mobile sign-out now calls Expo's
atomic `cancelAllScheduledNotificationsAsync`, which matches the requirement to
remove every AQI Clock lesson and announcement request instead of depending on
selective identifier matching. A regression test covers the call and the full
mobile suite passes 102/102. The checklist row remains open until a rebuilt APK
is installed and `dumpsys alarm` confirms zero notification alarms after
sign-out. The Windows sign-in boundary now detects a confirmed inactive profile
when initial sync fails and reports **Your account is inactive; contact an
administrator.** instead of a connectivity error; its regression test and the
149-test application suite pass (one interactive test skipped).

**Announcement/audience pass (2026-08-12):** PASS for `MOB-F03` and
`MOB-F09`. Five temporary published announcements exercised
everyone, AM, PM, teachers, and another class. The admin emulator showed the
category, relative time, unread dot, two-line body clamp, and standalone
eMasjid row without clipping at a 1.3 font scale. A temporary anonymous student
Data API session received everyone, AM, PM, and class-addressed rows but did
not receive the teachers row, confirming the RLS boundary. The app then applied
the enrolled Morning Year 1–5 plus AM-only audience: the owner confirmed on the
Pixel that only the everyone and AM controls appeared, while PM, teachers, and
other-class controls were absent. The temporary anonymous identity/device and
all five announcements were deleted, and the emulator font scale was restored
to 1.0. A temporary Wednesday timetable targeted to Morning Year 1–5 then
replaced the default schedule with an **Audience control — should disappear**
period on the Pixel. Removing that targeted row and syncing restored the normal
lessons plus the untagged **Naseehah & Break**, and the cancelled 22:08 control
notification did not arrive. The temporary schedule, timetable, and period
were deleted and production was re-queried with zero test rows remaining.

**Android acceptance split:** emulator functional, audience, exact-alarm, rescheduling, permission-denied, and forced-Doze checks are valid evidence. Notification drift and endurance timing require the stock-Android Pixel 9 Pro; emulator timing is not accepted as evidence. The app declares both `USE_EXACT_ALARM` and `SCHEDULE_EXACT_ALARM`; with exact-alarm access granted, record numerical scheduled and delivered timestamps and treat material multi-minute Doze batching as a failure, not expected behaviour.

## Audience-aware week schedule rollout gate

1. Release v0.11.3 and allow the estate to upgrade before applying `20260807120000_week_schedule_audiences.sql`. This moves legacy default-row saves onto the compatibility RPC; without it, applying the migration removes their PostgREST conflict arbiter and admin week-schedule edits fail.
2. Apply the v0.13.0 migration while every organisation still has only its seven default rows. Clocks and reads remain compatible, and v0.11.3 clients can continue editing default rows through the preserved two-argument RPC.
3. Upgrade every desktop and mobile client and confirm the estate is on a cache-v3 build.
4. Only then create or announce class-specific rows. The first second row for a weekday makes older clients' weekday-primary-key cache fail and freeze at its last good snapshot.

Verify a default plus class row on one weekday, a closed matching track, deterministic multiple-match selection, and teacher/default versus student/track clock and toast behaviour.
