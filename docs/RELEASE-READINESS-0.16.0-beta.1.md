# v0.16.0-beta.1 readiness

The owner authorized a Windows beta and fresh Android APK on 26 October 2026, following the proposal to test a real Naseehah broadcast before stable promotion. This authorizes an opt-in prerelease, not a new stable release.

## Automated verification

- Release solution build: passed with zero warnings/errors.
- Local .NET tests: 320 passed; one interactive application test and 64 Supabase tests skipped. The database tests require the disposable CI Supabase environment and must pass there before packaging.
- Native taskbar component build/tests: passed.
- Mobile: 127 tests passed across 24 suites; TypeScript and ESLint passed.
- Android Metro export: passed, including the WebView player and live-status hook.
- New listening coverage includes morning/evening Naseehah naming, exact period boundaries, offline/unknown availability, network/server failures, polling only during Naseehah, and discarding a response after the period ends.
- Four hosted CI jobs (mobile, Windows, fresh migrations/RLS, and migration rehearsal) are required by the release workflow before Windows packaging. Hosted results are pending at preparation time.

## Manual evidence and limits

- The official Al-Qalam mini-player loaded and reloaded in a standalone Windows player harness while the channel was offline. Closing the preview exited its process.
- Light/dark player pages and AQI backgrounds were visually inspected. AQI primary/secondary buttons were checked in dark mode.
- Lesson and simulated live-Naseehah toolbars were visually checked using the actual toolbar XAML in an isolated harness. The empty harness was not the final main-window layout, and a simulated LIVE state is not evidence of a live broadcast.
- No Android device is attached to the local adb session. No installed-APK, screen-lock, background playback or audio acceptance is claimed.
- Real broadcast playback, pause/volume, audio reconnect, full normal/compact windows, DPI scaling and installed-client acceptance remain beta checks. Existing Windows 10 taskbar limitations are unchanged.

## Distribution

- Source: feature branch based on the released main branch. Prior unmerged taskbar work remains on its original branch.
- Windows: v0.16.0-beta.1 prerelease only; v0.15.4 stays the latest stable release. The app's stable updater excludes GitHub prereleases.
- Android: preview APK, existing package/signing identity, version 0.16.0-beta.1 (code 2). It includes the notification quill fix and the new listening UI.
- Local chat attachments are excluded from Git and the EAS source archive.
- Before stable promotion, record one successful real live Naseehah session on Windows and Android, including background/screen-lock observations. Do not mark the pending items passed from automated or isolated-harness results.
