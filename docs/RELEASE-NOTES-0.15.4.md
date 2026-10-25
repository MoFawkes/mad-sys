# AQI Clock v0.15.4

- Optional Windows 10 taskbar display keeps the current lesson and remaining time visible. Hover for details; click, Enter or Space to open AQI Clock.
- Enable it in **Settings → Display → Windows 10 taskbar display** on 64-bit Windows 10. It is off by default. If needed, right-click the taskbar and select **Toolbars → AQI Clock**.
- Windows 11 retains compact mode and explains that the toolbar is unavailable.
- Fixes minimized-window activation, visible relaunch and intermittent taskbar fallback caused by snapshot timestamp precision.
- Improves installer startup hooks. Signing out of this desktop preserves sessions on other devices.
- Includes the holiday date-range closure fix from v0.15.3.

## Known limitations

Automatic startup and recovery are not fully validated. In a slow Windows 10 VM, the toolbar sometimes showed **open app** until clicked. If the toolbar is missing, use the taskbar's **Toolbars → AQI Clock** menu; if the feature causes trouble, turn it off in AQI Clock Settings and use the normal clock or compact mode.

Sampled cached countdown/lesson changes, activation, idle 100%/125%/150% scaling, an idle high-contrast case and one idle Explorer restart passed. Continuous countdown and typing focus during real meetings, receiver-side desktop/window sharing, hardware sleep/resume, 200% scaling, crowded/long-label layouts, and remaining installed-client/session/lifecycle cases are not fully validated. Earlier Zoom sharing checks covered the sender only.

The owner approved this stable release with those remaining risks accepted. The full evidence and untested cases are recorded in the [readiness record](https://github.com/MoFawkes/mad-sys/blob/main/docs/RELEASE-READINESS-0.15.4.md). Please report any issue with the app version, Windows version, taskbar scale/layout and steps to reproduce, without sharing private lesson or account data.

The previous [v0.15.3 release](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.3) remains available if needed. Avoid changing timetable data to troubleshoot a taskbar issue.
