# AQI Clock v0.16.0-beta.9

Admins can now create recurring reminders for teachers on Windows and Android.

- Open **Administration → Announcements → Recurring reminders** on Windows. Enter a title and message, choose weekdays, and select **After every lesson** or a fixed institute time.
- For example: every Friday, after each lesson, remind staff to fill in the weekly chart. Messages and days are configurable.
- Saved reminders can be edited, paused/resumed or deleted. Teachers and admins receive them through **Announcement notifications**; student devices do not receive them.
- Lesson-end reminders follow the actual timetable, including date replacements, and skip breaks and closed dates. Lessons finishing together produce one notification per reminder.
- Fixed-time reminders use the institute timezone and repeat on selected weekdays, including holidays. Different reminders at the same time remain separate messages.
- Includes beta.8's Windows administration sidebar and toolbar improvements, plus earlier beta features.

## Install

Use the Windows Setup executable or Android APK attached to this release. On Windows, enable **Settings → About → Include beta updates**, Save and restart to check for the update. Android retains its package/signing identity, with version name 0.16.0-beta.9 and version code 7.

This is an opt-in prerelease. Stable remains v0.15.5. The Windows installer is unsigned.

## Delivery and verification

Windows reminders require AQI Clock to remain running, including in the notification area. Mobile uses its existing seven-day scheduling window and earliest-60-notification limit. Offline clients retain their last synced plan until they reconnect; pauses, deletion and timetable edits take effect after sync.

Local validation passed: 85 domain, 261 Windows application, 16 SQLite/integration and 391 Supabase tests, with one opt-in interactive test skipped. All 162 mobile tests, TypeScript and ESLint passed. Actual WPF layouts and action bindings were checked in light/dark themes and at a small window size.

The additive staff-only database migration must be deployed before the updated clients. Hosted release checks and package verification are recorded in the release publication notes. Installed Windows/Android reminder delivery, update installation and the existing device acceptance checklist remain pending before stable promotion.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.8) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.5)
