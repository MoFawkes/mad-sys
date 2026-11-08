# AQI Clock v0.16.0-beta.2

An opt-in beta for Windows and Android. Windows stable remains v0.15.4.

- Today's periods now share one row when start/end times match. Sessions appear side by side with dividers in a consistent order. Shared names such as Isha + Naseehah appear once, while different lesson names and all session details stay visible.
- **Settings → Report a problem** lets students and teachers describe an issue, explain the expected behaviour, and attach screenshots or files. Up to five attachments are allowed, totalling 10 MB.
- Review the report before sharing. App details (version, OS, session type, sync state and class selection) are optional. Account credentials, account emails, source file paths and raw logs are not collected automatically. Selected attachments are included as chosen.
- Windows saves a ZIP where you choose; Android opens the system share sheet. Send the ZIP to the person helping with AQI Clock. The app has no feedback inbox and sends nothing automatically.
- Includes the official Al-Qalam eMasjid player, quiet lesson toolbar/live Naseehah indicator, player themes, and Android quill notification icon from beta.1.

## Install

Windows: use the Setup executable below. This unsigned beta is not offered by the stable automatic updater. The embedded player requires Microsoft Edge WebView2 Runtime; an Open website fallback is available.

Android: install the APK below as an update. It retains the package/signing identity, with version 0.16.0-beta.2 and version code 3. No uninstall or account reset is required.

## Verification and remaining beta checks

Local Release build passed with zero warnings/errors. 324 local .NET tests and 138 mobile tests passed, alongside TypeScript and lint. The 64 environment-dependent database tests run in hosted release CI; one interactive desktop test remains opt-in. ZIP tests cover attachment size/count limits, duplicate filenames, safe archive paths, changed attachments, optional app details and source-path/credential omission.

The grouped Windows timetable was inspected using the real clock view in an isolated sample-data harness. The Windows feedback form was visually inspected; the interactive picker/save check was interrupted before completion, so automated archive tests are not claimed as full installed-client export acceptance. Android file picking/sharing still needs a device check; the available emulator's system image is incomplete.

Real live audio, reconnect/volume, Android screen-lock/background listening, installed-client feedback export/sharing, and Windows display scaling remain beta checks. Existing Windows 10 taskbar limitations remain. Please test one real Naseehah session and send a sample feedback ZIP from each platform before stable promotion.

[Previous beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.1) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.4)
