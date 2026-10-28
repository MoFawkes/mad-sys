# AQI Clock v0.16.0-beta.8

A Windows interface update, including beta.7's automatic Naseehah reminders.

- Administration now uses a left sidebar instead of a long row of tabs. Sections are grouped into **Schedule**, **People & access**, **Communication** and **Activity**, with icons, selection highlighting and a clear page heading.
- **Classes / Audiences** is now **Classes**, and **Profiles / Audiences** is **Staff access**. Their existing editing controls are retained.
- The clock toolbar returns to clean icon-only buttons for listening, compact mode, pinning, settings and editing timetables. Icons are larger, with hover labels and accessibility names. Pin state and the live Naseehah listening highlight remain visible.
- The sidebar scrolls on shorter windows. All nine administration sections remain available.

## Install

Use the Windows Setup executable below, or enable **Settings → About → Include beta updates**, Save and restart AQI Clock to check for it.

This update changes Windows only. [Android beta.7 APK](https://github.com/MoFawkes/aqi-clock-releases/releases/download/v0.16.0-beta.7/AqiClock-0.16.0-beta.7-android.apk) includes the automatic Naseehah reminders. Stable remains v0.15.5; this is an opt-in prerelease.

## Verification

Release build and 247 Windows application tests passed locally; one interactive sign-in test is opt-in. Rendered the actual WPF administration and clock layouts with isolated sample data in light and dark themes, including a short/narrow admin window. Checked navigation to all nine sections and synchronisation in both directions between the sidebar and content selection. This checks the real layouts with sample data, rather than installed-client or live editing acceptance.

Installed updater, real-device reminder delivery and the existing live-audio/feedback/DPI acceptance checks remain before stable promotion. The installer is unsigned.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.7) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.5)
