# AQI Clock v0.16.0-beta.5

A Windows layout hotfix for beta.3.

- Grouped lessons stay together as readable lesson/session blocks with dividers. They wrap within the timetable when the window is narrow, instead of spreading into different columns or clipping off the side.
- Session labels are larger and the timetable gets more room in wide windows.
- The clock, current lesson and next lesson stay together. The current-lesson box no longer stretches into a large empty panel between lessons. The clock panel scales down when needed so the next lesson stays visible at minimum height.
- Removes the blue selected-row outline from the timetable. Current-period highlighting remains.

## Install

Use the Windows Setup executable below. If you installed beta.3 and enabled **Settings → About → Include beta updates**, Save and restart AQI Clock to check for this update.

This is a Windows-only hotfix. Android is unchanged: [Android beta.3 APK](https://github.com/MoFawkes/aqi-clock-releases/releases/download/v0.16.0-beta.3/AqiClock-0.16.0-beta.3-android.apk). Stable remains v0.15.4, and this prerelease is excluded from stable-only updates.

## Verification

Release build and all 233 local Windows application tests passed; one interactive desktop test remains opt-in. Rendered the actual WPF clock/timetable with isolated sample data at 1440, 800 and 680 pixels of content width, in both themes and with active and idle lesson states. Narrow-width inspection caught and corrected the list measuring rows wider than its viewport. This is visual/layout verification, not an installed-client update test.

Existing beta device checks for live audio, attachment picking/export/sharing, installed updater behaviour and Windows display scaling remain before stable promotion. No timetable data or Android code changes are included.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.3) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.4)
