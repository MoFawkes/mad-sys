# AQI Clock manual OS-integration checklist

This checklist is the ADR-011 acceptance script for Windows surfaces that are not reliably covered by unit tests. Run the full checklist on Windows 10 and Windows 11 before the Phase 8 pilot. Record the app commit, Windows version, tester, date, and result for each run.

## Test record

- App commit:
- Windows version/build:
- Tester and date:
- Supabase target: local / cloud
- Result: not run / pass / fail
- Notes or issue links:

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

- [ ] Create at least two classes with distinct names and sort orders.
- [ ] Tag at least one period with one class and leave another period untagged or tagged to the other class.
- [ ] Have an active Admin account available for the Admin-window checks.
- [ ] Prepare announcement content suitable for a future scheduled publication, class targeting, and an HTTPS e-Masjid link.
- [ ] Complete a sync so the student picker has current classes and period tags in its local cache.

### Sign-in fork

- [ ] Cold start opens **Choose how to continue** (`RoleChoiceWindow`) instead of opening email/password sign-in directly.
- [ ] **Teacher** opens the existing email/password `SignInWindow`, and valid teacher credentials reach the main clock with the existing teacher behavior unchanged.
- [ ] From `RoleChoiceWindow`, choose **Teacher**, then close `SignInWindow` without signing in. The app returns to `RoleChoiceWindow` instead of exiting.
- [ ] **Student** opens `StudentClassPickerWindow` without requesting a personal identity or credentials.

### Student classes and optional Naseehah

- [ ] Select multiple classes using the checkbox rows. With no class selected, **Start student session** remains blocked and shows the inline `Select at least one class.` error.
- [ ] Confirm the independent optional Naseehah checkboxes allow all four states: AM only, PM only, both, and neither.
- [ ] Select a PM-running class and AM Naseehah only. Its class-tagged PM period notification still fires; an AM-audience announcement appears/notifies and a PM-audience announcement does not.
- [ ] Repeat with neither Naseehah option selected. Class-tagged period notifications remain active, while AM- and PM-audience announcements are both suppressed.
- [ ] With only class A selected, confirm periods and specific-class announcements tagged only to class B do not notify or appear.
- [ ] Click a period or announcement toast during an active student session. It activates the running main window rather than reopening sign-in.
- [ ] Restart after a student session. No selected classes, Naseehah choices, or student identity survive; the app asks how to continue again.

### Admin — Classes / Audiences

- [ ] Add, edit, save, and delete an unreferenced class using the per-row controls.
- [ ] Save two classes with the same name or **Order** value. The Admin window shows `A class already uses that name or sort order.` instead of crashing.
- [ ] Target an announcement at a class, then attempt to delete that class. The Admin window shows `This class is referenced by an announcement. Reassign or delete the announcement first.` instead of exposing an exception.
- [ ] In the period-tags grid, enter one or more valid class names in the comma-separated **Classes** column and choose **Save tags**. Sync/reload and confirm the assignments persist.
- [ ] Enter an unknown class name while saving period tags and confirm the row reports a useful inline error without losing other saved tags.
- [ ] In **Profiles / Audiences**, confirm Teacher and Admin profiles remain editable and Graduate remains visibly unavailable/coming soon.

### Admin — Announcements

- [ ] Compose an announcement for a specific class with today's date and a future `HH:mm` publish time. It is scheduled and remains absent from active readers and notifications until that time.
- [ ] Confirm the scheduled announcement is suppressed for a student session that selected a different class and becomes visible/notifiable for the selected target class once due.
- [ ] **Known open issue — expected to fail:** publish an announcement with a valid HTTPS e-Masjid link and inspect the reader. The current reader does not render the stored link; route this back for implementation before merge.
- [ ] Try a relative, malformed, or non-HTTPS e-Masjid link. Publishing is blocked with `The e-Masjid link must be a valid HTTPS URL.`
- [ ] Delete an announcement that has a `PublishAt` value. It moves out of the active view into **History**, and its original publication date remains unchanged.
- [ ] On a soft-deleted History item, confirm **Publish now** is disabled and cannot resurrect the announcement.
- [ ] Confirm **Graduates** is absent from the Audience picker. This is intentional while Graduate sign-in and delivery are deferred; do not offer this audience until a Graduate device role can receive it.

The AM/PM and class-overlap scheduler scenarios are also covered by the automated application tests. Record the PR CI run in **Notes or issue links**; do not substitute CI for the interaction checks above.

### Audience-aware Light/Dark presentation

- [ ] Inspect `RoleChoiceWindow` and `StudentClassPickerWindow` in Light, Dark, and System modes. Switching themes updates each open/new window without restarting.
- [ ] In Light mode, confirm headers use navy `#112549`, primary actions use blue `#2E6DD8`, the background uses cream `#F4F0E6`, and secondary text uses grey `#6B7280`.
- [ ] In Dark mode, confirm navy surfaces and cream text remain readable. Pay particular attention to headings using dark `HeaderBrush` (`#0B1933`) against the window/card surfaces and record any insufficient contrast as a failure.
- [ ] Inspect Main, Admin, Announcements, Role Choice, and Student Class Picker in both Light and Dark modes. Confirm there is no default-white chrome, clipped text, unreadable selection state, or binding-error log entry.
- [ ] Re-check the Admin `DataGrid` background and main-window frame border in both themes.

`HighlightBrush` is defined as gold in both theme dictionaries but is not currently consumed by a control. Its absence on these screens is therefore not a visual failure.

### Release decision

The current untagged v0.9.6 candidate is a small accepted theme fix based on `58c136a`; PR #1 is a substantially larger sign-in and session-behavior change that still requires this checklist. Decide whether v0.9.6 waits for the audience-aware verification or can be tagged while this work ships in a later version. Once the owner decides, record the choice in the handoff section of `PROJECT-STATUS.md`.
