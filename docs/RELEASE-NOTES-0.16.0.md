# AQI Clock v0.16.0

This release brings the v0.16 beta series to the stable Windows and Android channels.

## What's new

- **Recurring teacher reminders:** Windows admins can open Administration → Announcements → Recurring reminders, enter a custom title/message, choose weekdays, and select each lesson's end or a fixed institute time. Edit, pause/resume and delete saved rules. Lesson-end reminders follow timetable replacements, skip breaks and closed dates, and merge simultaneous lesson endings. Fixed-time reminders also run on selected weekdays during holidays. Teachers and admins receive them through Announcement notifications; students do not.
- **Live listening:** the official Al-Qalam eMasjid player is available within the app, with a discreet listening entry and confirmed live-broadcast highlighting during Naseehah.
- **Readable timetables:** simultaneous lessons are grouped into clear session rows, with improved wrapping and clock layout at small Windows sizes.
- **Feedback export:** create feedback reports with ZIP attachments on Windows and Android.
- **Windows navigation:** administration has a grouped icon sidebar, and the clock toolbar has larger icons, tooltips and accessible action names.
- **Automatic Naseehah reminders:** students' AM/PM choices subscribe to start reminders derived from assigned timetables, respecting date overrides and Announcement notifications without duplicating class-start alerts.
- **Update preferences:** Include beta updates remains optional and off by default. Opting out never downgrades the installed app.

## Install and update

Use the Windows Setup executable or Android APK attached to this release. Existing Windows installations receive this stable release through the updater; apply the downloaded update when prompted. Android's update check opens the APK download for installation.

Android retains package `com.mofawkes.aqiclock` and the existing signing certificate, with version name 0.16.0 and version code 9. Beta.10 users can update directly to this stable version. The portable Windows ZIP is also attached.

## Verification and practical limits

The recurring-reminder production migration is already deployed and verified, including RLS, admin-only writes, auditing and Realtime membership. Production security advisors report no errors.

The verified beta source passed 85 domain, 261 Windows application, 16 SQLite/integration and 391 Supabase tests; one opt-in interactive Windows test was skipped. All 162 mobile tests, TypeScript and ESLint passed. Actual WPF layouts/action bindings were checked in light/dark themes and at small window sizes. Stable-tag hosted release checks and package verification are recorded alongside this release.

Windows notifications require AQI Clock to remain running, including in the notification area. Mobile uses a seven-day notification window with an earliest-60-request limit. Offline devices retain their last synced plan until reconnecting; edits, pauses and deletion take effect after sync. Notification permissions and OS restrictions apply.

The Windows installer is unsigned. Installed-device reminder delivery, update installation, attachment sharing, live audio/background behaviour and DPI acceptance remain pending in the documented manual checklist; this release does not claim those checks have been completed.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.10) · [Previous stable](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.5)
