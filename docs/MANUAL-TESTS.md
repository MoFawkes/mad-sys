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
