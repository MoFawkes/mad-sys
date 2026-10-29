# AQI Clock v0.16.0-beta.3

Windows and Android now have **Settings → About → Include beta updates**, off by default.

- Windows: turn it on, Save, then restart AQI Clock. Future beta releases download through the existing updater and apply when you restart. Turning it off prevents applying a queued beta on exit.
- Android: checks the chosen release channel once a day when the app is foregrounded and prompts once for each newer version. **Check for updates** also checks immediately. Choose Download to open the APK download; approve Android installation to finish updating.
- Turning beta updates off keeps your installed version until a newer stable release is available. It does not downgrade the app or reset your account.
- Includes beta.2's grouped timetable rows, feedback ZIPs with up to five attachments (10 MB combined), and beta.1's official Al-Qalam eMasjid player, live Naseehah indicator and Android notification icon fix.

## Install this update

Install beta.3 once using the Windows Setup executable or Android APK below to get the new preference. Earlier versions do not have the beta opt-in setting. Android retains its package/signing identity, with version 0.16.0-beta.3 and version code 4; no uninstall is required.

This is an opt-in prerelease; Windows stable remains v0.15.4. The Windows installer is unsigned. The embedded player requires Microsoft Edge WebView2 Runtime, with an Open website fallback.

## Verification and remaining beta checks

233 Windows application tests and all 148 mobile tests passed locally, with TypeScript and lint clean. One interactive desktop test remains opt-in. Added coverage includes stable-only selection, version ordering, no downgrades, opt-in persistence, trusted APK URLs, daily checks, stale responses after opt-out, and offline behaviour. Hosted Windows/mobile/database checks gate packaging.

Installed-client beta update/download/install acceptance, feedback picking/export/sharing, real live audio, reconnect/volume, Android screen-lock/background listening and Windows display scaling remain device checks. Existing Windows 10 taskbar limitations remain. Test a real Naseehah session and sample feedback ZIPs before stable promotion.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.2) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.4)
