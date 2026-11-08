# AQI Clock v0.16.0-beta.1

This is an opt-in beta for Windows and Android. Windows stable users remain on v0.15.4.

- Listen to Al-Qalam Institute inside AQI Clock using eMasjid Live's official mini-player. Playback starts only when you press Play.
- A small headphones icon opens the player. During a scheduled Naseehah, a confirmed live broadcast highlights the listening option. Status is checked once a minute during Naseehah; offline or unknown status does not show LIVE.
- Windows toolbar labels hide during teaching periods so the lesson clock stays prominent. In compact mode, the listening icon appears only for a live broadcast during Naseehah.
- Windows selects the official light/dark player theme when opened or reloaded; Android uses the dark player with AQI's navy background. eMasjid branding and playback controls remain intact.
- Android notifications now use a transparent quill icon rather than a solid white circle.

## Installation

Windows: download the Setup executable from this prerelease. The installer is unsigned; this beta is not offered through the stable automatic updater. The in-app player requires Microsoft Edge WebView2 Runtime. If unavailable, the player offers an Open website fallback.

Android: install the attached APK as an update to AQI Clock. It uses the existing application ID and signing key, with version name 0.16.0-beta.1 and version code 2. No uninstall or account reset is needed.

## Beta checks still required

The official player loaded and reloaded successfully while the channel was offline. Live playback, play/pause/volume, reconnecting during a broadcast, and Android screen-lock/background listening have not yet been accepted on devices. Do not rely on background listening in this beta.

Windows lesson/live toolbar states were checked in isolated layout previews, not full installed-client acceptance. Full-window normal/compact layout, light/dark modes and display scaling still need installed-beta checks. Existing Windows 10 taskbar limitations from v0.15.4 remain.

Please test one real live Naseehah session on both devices before promoting to stable. Keep [v0.15.4](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.4) available as the Windows fallback; do not alter institute timetables to manufacture a test period.
