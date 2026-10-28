# AQI Clock v0.16.0-beta.7

Windows and Android now send daily Naseehah reminders automatically from the timetable.

- Student AM/PM Naseehah choices subscribe to reminders for that group, including a Naseehah scheduled in another class's timetable. No daily announcement needs to be written.
- Turn on **Announcement notifications** and select the matching AM/PM option. Before noon is AM; noon onwards is PM, using institute time.
- Combined blocks such as **Isha + Naseehah** notify at the block's start. Shared starts across classes produce one reminder, without duplicating the selected class's start alert.
- Closed dates and replacement timetables control reminders. Existing class-period alerts retain their lesson notification settings.
- Updated student setup text explains the options. Includes the earlier beta's listening player, grouped timetable layout, feedback export and optional beta updates.

## Install

Use the Windows Setup executable or Android APK attached to this release. Existing beta users can enable **Settings → About → Include beta updates**; on Windows, Save and restart to check. Android retains its package/signing identity, with version name 0.16.0-beta.7 and version code 6.

This is an opt-in prerelease. Stable remains v0.15.5. The Windows installer is unsigned.

## Verification

Local automated validation passed: 247 Windows application tests and 79 domain tests, with one interactive Windows test skipped, plus all 157 mobile tests. Mobile TypeScript and lint passed. Coverage includes independent AM/PM subscriptions, cross-class reminders, same-time deduplication, restarts, notification setting changes, date closures/replacements, institute timezone conversion and cancellation of stale mobile requests.

The local Release solution build also passed 15 integration tests. The 64 database tests require a local stack and are covered by the hosted release gates instead.

All four [hosted release checks and Windows packaging](https://github.com/MoFawkes/mad-sys/actions/runs/40788153645) passed. The [Android preview build](https://expo.dev/accounts/denovoorders/projects/aqi-clock/builds/e88360c0-76aa-4eee-b9ec-44e71bc8a00d) completed successfully. Verified the Windows embedded beta.7/source version and updater manifest size/hash, and the APK package/version/signature against the previous beta. SHA256SUMS.txt records the uploaded asset digests.

Real installed-device reminder delivery, Android background delivery, update/install acceptance and the existing live-audio/feedback/device checks remain pending. Windows reminders require AQI Clock to remain running, including in the notification area. Mobile uses its existing seven-day scheduling window, refreshed on sync and reconciliation.

[Previous Windows beta](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.16.0-beta.5) · [Stable fallback](https://github.com/MoFawkes/aqi-clock-releases/releases/tag/v0.15.5)
