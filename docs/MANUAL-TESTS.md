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
