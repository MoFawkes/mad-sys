# AQI Clock — Architecture / Engineering Status

Last updated: 2026-08-23

This is the shared handoff document for Fable 5 (Architecture) and Codex
(Implementation / Engineering). Keep it current when scope, release state,
risks, or ownership changes. `TASKS.md` remains the delivery checklist,
`CHANGELOG.md` remains the release record, and `docs/MANUAL-TESTS.md` remains
the acceptance script.

## Live state

| Area | State |
|---|---|
| Staff pilot | v0.14.0 is published; feature/device acceptance is complete, while the real-machine v0.13.3 → v0.14.0 update/restart round-trip remains open |
| Public release channel | v0.14.0 is Latest on `MoFawkes/aqi-clock-releases` |
| Source | `main` at `0889e44`; annotated tag `v0.14.0` includes PR #28 and the final acceptance record |
| Production backend | Migration `20260807120000` applied; audience-aware schema and RPCs verified |
| Latest release | v0.14.0 — unified desktop teacher-feedback, interruption reflow, and Expo mobile companion |
| Next release | v0.15.0 implementation complete; manual/deployment gates remain |
| Candidate CI | All four required checks passed for final acceptance-fix PR #28 |
| Release workflow | Run `32755130666` is green; v0.14.0 was published at 2026-08-24 18:42:41Z with full, delta, setup, portable, and stable-manifest assets |

## Release split — decided 2026-08-02

v0.11.0 is the **desktop** release; the Expo companion moves to **v0.12.0**.

Release train update, 2026-08-08: v0.11.3 supplies the compatibility RPC,
v0.13.0 introduces audience-aware week schedules and cache schema v3, and the
held Expo companion is renumbered to v0.14.0 so it follows those prerequisites.

Release train update, 2026-08-18: the desktop teacher-feedback fixes and held
mobile companion now ship together as v0.14.0. Desktop MinVer and the existing
mobile package/app metadata therefore converge on the same version number.

The desktop work was previously recorded as unable to ship alone because enrolment needs
`enroll_student_device` and the student RLS migrations. That dependency is **already
satisfied**: production carries all seven migration versions (audited 2026-08-01) and `main`
carries them since PR #2. Nothing else couples the two — `.github/workflows/release.yml`
publishes only the Windows Velopack artifact, and the mobile APK is distributed by link, so
it stays invisible to users until one is shared. Teachers therefore do not need to wait for
mobile device acceptance to receive fixes they asked for.

## v0.14.0 unified desktop and mobile state

- Phases 0–10 are implemented: the original mobile scope, visual alignment, isolated/admin-distributed join codes with desktop QR and mobile sharing, revoked-device recovery, and desktop-brand icon parity.
- The additive student-device migration and signup hook are covered by the release-gating Supabase matrix; the existing staff/admin matrix remains in scope.
- Mobile automated gates are Jest, TypeScript, and ESLint in their own CI job. On 2026-08-18, all 105 Jest tests, TypeScript, and ESLint passed at version 0.14.0; the full .NET run passed 218 tests with one interactive test skipped. Existing Supabase required checks remain unfiltered.
- Distribution is an internal Android APK from the EAS `preview` profile; iOS and store submission are out of scope.
- The 2026-08-01 desktop follow-up is merged on `main`: staff sessions tolerate boot-time network races and refresh proactively; desktop students use anonymous enrolment with persisted choices and student-scoped sync; Admin/Settings sizing and editor interruptions are corrected. It ships as v0.11.0 on its own — see the release-split section above.
- EAS preview build `f2b8871d-919f-41df-96e6-104b621cbee4` from `357e48f` shipped with `POST_NOTIFICATIONS` and `RECEIVE_BOOT_COMPLETED` but **neither exact-alarm permission**: `expo-notifications` 0.32.17 no longer declares `SCHEDULE_EXACT_ALARM` for its consumers, so Android 12+ fell back to inexact alarms, which Doze batches to roughly nine-minute granularity.
- Superseded by build `47b10fa9-a2db-4958-8291-eb3779583c7c` from `3f20549`, which declares both `USE_EXACT_ALARM` and `SCHEDULE_EXACT_ALARM` (verified in its merged manifest) and carries the student-session fix. SHA-256 `9B9C80DB17D546BDB4AA6912F9C0DF9AF3E7FF26F1083817ABEE3A3DB64BFDF8`. The subsequent Pixel 9 Pro timing and endurance suite passed by owner confirmation on 2026-08-12.
- Acceptance APK `f9f0a6a3-fc7c-4d2c-aa4d-341d50170216` was built from `d3c5c34` with the EAS `preview` profile: version 0.14.0 (versionCode 1), SDK 54, fingerprint `b61d5458448229b521c390e5c3a9174512487374`, SHA-256 `075061C9FC97F5B8D54C0B858595FCF8400ACCC62EDEE8739781A3F9BBB35423`. The paired local Windows acceptance build was `0.13.4-dev.2+5a237c6`, SHA-256 `A80A8627F480305CFF7130FBA70C0CA7ED3A1E707BBA99FF123B000D2C2D4F64`.
- The post-PR #17 Windows retest build is `0.13.4-dev.5+62d1434`, SHA-256 `BABBF0C681FAB2D56FA275FF57B166A5EA88F90798D2D8F009C95E6EA2340D95`. Its isolated-profile pass confirmed teacher sign-in Back, Esc, and title-bar X plus student-picker Back all return to role choice without exiting. It is untagged and exists only for acceptance.
- The reflow acceptance build is `0.13.4-dev.9+b3454c6`, SHA-256 `5F1160B20274AED90C741104C4DA76DBB31EE7BCA193B4495ECE71E2FFE2447E`, built Release from `main` at `b3454c6` on 2026-08-19 with 0 warnings and 0 errors. It is the first Release binary containing the interruption reflow tool: the previous Release output was `0.13.4-dev.7+df6bd64`, one commit before `ed50c17`, so no earlier local binary could have exercised the reflow rows. Desktop rows D1-D5 in `docs/MANUAL-TESTS.md` must run against this build. It is untagged and exists only for acceptance; the published v0.14.0 artifact is rebuilt from the tag by `release.yml`.
- Final desktop D1 acceptance passed on 2026-08-23 against `0.13.4-dev.16+00a2ab85e417497cffbe9cdf4f6bc734924a375f`, SHA-256 `936C8C93E0AED64C7161699460564FBD9617AEF50354A6580F21F3654EEAFD56`. The inactive staff account received the exact inactive-account message and was reactivated immediately. PR #28 merged the fix to `main` at `011aac1`; all four required checks passed.
- The 2026-08-02 emulator notes referred only to mutable source line numbers, which now land in the APK-evidence prose rather than checklist rows. That evidence is untraceable, credits no current acceptance row, and must not be relied on. Re-run the emulator checklist using the stable `MOB-F*` and `MOB-A*` identifiers in `docs/MANUAL-TESTS.md`; Pixel-only timing and endurance rows use `MOB-T*`.
- Production reconciliation on 2026-08-01 found all seven migration versions and the complete student-device schema already applied. The hosted signup gate was verified against production intent: public email signup returned 403 **Public signup is disabled**, while an anonymous identity enrolled successfully and could select its own `student_devices` row under RLS.
- The Pixel 9 Pro timing and endurance suite (`MOB-T01`–`MOB-T05`) passed by owner confirmation on 2026-08-12. `MOB-T06` is accepted as a scoped pass for stale-class cancellation and reboot-driven rescheduling; the explicit residual evidence gap for 19–20 August remains documented. All mobile functional and emulator alarm rows now pass. Final `MOB-F10` evidence used EAS build `e815e646-e89c-4648-9acb-d2ce230e9396` from `38468a2`, SHA-256 `FC98A2AF9AA8CF7FA9E05ADE197494BB7CF2398CA6C736468FFD95A9ED53C324`: in-app teardown returned to role choice, left no app-data files, and reduced package alarms to zero without clearing app data. The resulting anonymous identity and device row were deleted; the Pixel was untouched.
- The published v0.14.0 release carries desktop Back/Esc/title-bar navigation, merged multi-class agendas, shared-timetable deduplication, combined notifications, mobile stale-alarm reconciliation, and the interruption reflow tool. Feature/device acceptance rows are closed. Publication is verified separately from installed-update acceptance: a real v0.13.3 machine must still confirm that v0.14.0 is offered and applies on restart. The Admin clash warning was withdrawn because legitimate staggered timetables overlap permanently; revisit it with the Part 4 generator model.
- Scope added on 2026-08-18: v0.14.0 also carries a schema-free desktop interruption reflow tool so term can start without hand-adjusting every later lesson when Maghrib moves. It inserts named non-lesson rows or shifts a selected row and all later rows, closes seams, rejects midnight crossings atomically, and continues saving ordinary periods through the existing whole-list RPC. The full block/anchor generator remains v0.15.0 work.
- Pre-wide-rollout risks: Supabase realtime volume/tier, anonymous-user cleanup, stale `last_seen_at`, and the desktop untagged-period notification semantic difference.

## v0.9.3 scope

- WPF-UI 4.3 Fluent presentation across authentication, recovery, Settings,
  Admin, and the main-window interior.
- Navy `#24457A` accent, Light/Dark/System themes, semantic status colours,
  PerMonitorV2 DPI awareness, and Windows 11 Mica with Windows 10 fallback.
- Quill-and-inkwell branding across application, tray, installer, Settings,
  and notification identity.
- Existing MVVM behaviour, native Admin grids, and the 320×80 compact-mode
  chrome contract remain intentionally preserved.
- The generated multi-window design image discussed on 2026-07-18 is a visual
  concept, not the v0.9.3 acceptance baseline.

## v0.9.5 scope

- Resilient startup keeps REST sync and heartbeat alive when Supabase Realtime
  is temporarily unavailable, and independently retries the subscription.
- Realtime socket upgrades no longer inherit the bundled client's legacy
  `Authorization: Bearer <api key>` header, which is invalid for modern opaque
  `sb_publishable_*` keys.
- The plain main window paints its own theme background; second instances
  dispose an unowned mutex without releasing it.
- Owner decision 2026-07-18: the generated concept is now the main-window
  design target. Normal mode uses a two-pane lesson/period composition,
  concept-aligned toolbar and status strip; Compact remains exactly 320×80.
- The announcements flyout is scoped to the normal-layout content row, with
  explicit title/action columns so it cannot collide with its admin action or
  cover the status bar.
- Settings/Admin fine-detail alignment to the concept remains follow-up
  backlog and is not part of v0.9.5.

## v0.9.6 candidate scope

- Theme all Admin `DataGrid` backgrounds, rows, cells, headers, dividers, and
  selection states so Dark mode does not expose WPF's default white surface.
- Restore the plain main window's dynamic `WindowBrush` after WPF-UI updates
  its titlebar, keeping the 10px client margin light in Light mode.
- Preserve WPF-UI's implicit Fluent button template in the locally conditional
  "Edit timetables" and "Compose / manage" styles.

## Architecture — Fable 5

Current:

- Phases 1–7 are complete.
- Phase 8 packaging, updater, recovery protocol, and production backend are
  implemented.
- The v0.9.3 scope is the Fluent polish already merged to `main`; it is not a
  new information-architecture redesign.
- `MainWindow` deliberately remains a plain WPF `Window` because compact mode
  switches to frameless chrome at runtime. Unlike `FluentWindow`, it must
  explicitly inherit the application foreground and synchronize native DWM
  titlebar state when the WPF-UI theme changes.
- The approved concept now governs the main window only. Settings and Admin
  retain the v0.9.3 Fluent surfaces until their separate backlog pass.

Defects fixed for v0.9.5:

- A Realtime 403 could abort `SyncService.StartAsync` before REST sync or
  heartbeat startup, leaving the process permanently Offline. Realtime is now
  retried independently.
- The bundled C# client treated the modern opaque publishable key as a legacy
  Bearer JWT during the WebSocket upgrade. Direct query-key probes returned
  101, while v0.9.4 still received 403 after restart. Realtime-only inherited
  headers are now suppressed; the live repaired build connects successfully.
- The root Grid margin exposed the plain Window's default white background in
  Dark mode. The Window now uses the dynamic `WindowBrush`.
- Second-instance launch released a named mutex it did not own and raised an
  unhandled `ApplicationException`. Ownership is now explicit and the
  non-owning path exits with code 0.
- The redesigned announcements overlay used a root-level panel and a
  collision-prone `DockPanel` header. It now overlays only the content row and
  uses explicit columns for the title, admin action, and close action.

Next architecture inputs:

- Record any post-v0.9.3 design/functionality notes as explicit backlog items
  with acceptance criteria.
- Close the release trackers after Engineering publishes v0.9.5.
- Keep code signing, Supabase plan review, and .NET LTS migration in the
  pre-wide-rollout gate.

## Implementation / Engineering — Codex

Completed:

- Replaced the invalid legacy Supabase client key in GitHub Actions with the
  current client-safe publishable key and verified the Auth endpoint accepts it.
- Passed the local Release test gate: 120 tests passed; the 14 local-stack
  Supabase tests and one credential-dependent smoke test skipped as designed.
- Published v0.9.3 after both tag-bound GitHub gates passed.
- Verified the public portable artifact digest, embedded v0.9.3 product version,
  publishable client key, production cloud response, and updater repository.
- Fixed the v0.9.3 main-window Dark-theme regression without changing the
  compact-mode architecture: `TextBrush` now inherits through the root visual,
  and WPF-UI's public window-background manager updates native titlebar chrome.
- Visually verified Dark and Light in Normal and Compact modes. Both compact
  states remained exactly 320×80; DWM reported immersive dark titlebar state
  `1` in Dark/System and `0` in Light.
- Passed the v0.9.4 local Release gate: 120 tests passed; environment-dependent
  tests skipped as designed.
- Published v0.9.4 after both tag-bound gates passed and independently verified
  its digest, embedded version, publishable key, cloud response, updater target,
  and full/delta stable index.
- Decoupled Realtime startup from REST sync and heartbeat, added subscription
  retry/backoff and heartbeat exception containment, and covered all three
  recovery paths with focused tests.
- Corrected modern publishable-key handling for the Realtime upgrade and
  verified the production socket, REST refresh, and `Synced · just now` state
  together in the repaired build.
- Fixed the Dark-mode window border and second-instance mutex ownership defect;
  the repaired second instance activated the installed app and exited in
  563 ms with exit code 0 and no crash event.
- Implemented the approved main-window concept in normal and compact modes,
  including the two-pane period-state layout, Fluent toolbar/status icons,
  relative sync detail, empty-day treatment, and exact 320×80 compact strip.
- Passed the v0.9.5 local Release gate: 124 tests passed, 15
  credential/environment-dependent tests skipped as designed.
- Captured Dark/Light Normal, Dark/Light Compact, empty-day, lesson-state, and
  announcements-overlay evidence under `artifacts/v0.9.5-ui/` (local,
  intentionally ignored).
- Owner accepted the candidate subject to repairing the announcements flyout.
  Engineering fixed its header collision and status-bar overhang in `da6983c`,
  then captured corrected Dark-mode evidence with production sync online.
- Implemented the v0.9.6 surface-consistency fixes in `3a558e5`. Dark Admin
  evidence confirms the period grid and empty surface are themed; Light main
  evidence confirms the client margin no longer forms a black frame.
- Passed the v0.9.6 local Release gate: 124 tests passed, 15
  credential/environment-dependent tests skipped as designed.
- Published v0.9.6 from `d52249f` after main CI run `29774771246` and
  tag-bound release run `29775056726` passed. Independently verified the
  public portable digest, full/delta stable index, embedded product version,
  publishable client configuration, live Auth response, and updater target.
- Added the production-like migration rehearsal as a third CI job
  (`40624aa`): reset to the released v0.9.6 baseline, load production-like
  staff/announcement data, apply the pending audience migration incrementally
  as `db push` will in production, assert the migrated schema/data, then run
  the full RLS/behaviour matrix against the upgraded database. Release tags
  require it automatically through the existing `needs: gates` chain.
- Fixed frozen-migration drift found while building the rehearsal: the
  Teacher-rename click-through had edited the already-applied
  `20260716000300` trigger migration, which production would never re-run.
  The frozen file is reverted and the renamed guard wording now ships in the
  audience migration via `create or replace`.

In progress / next:

- Finish the hands-on System and 150% DPI portions of the UI/DPI matrix.
- Perform the v0.13.3 → v0.14.0 auto-update check on a real pilot machine:
  confirm the update is offered, downloads, and applies on restart.
- Complete the installer/update/uninstaller round trip and record results in
  `docs/MANUAL-TESTS.md`.
- Track the pre-existing timetable-editor period-cell commit quirk as
  post-v0.10.0 backlog; it is not an audience-aware release blocker.
- After publication, synchronize release trackers/status and retain the
  remaining System/150% DPI and pilot update checks as post-publication work.

## Release gates

### v0.15.0 generator and delivery-evidence gate (2026-08-27)

| Gate | Result |
|---|---|
| Domain | 74 passed |
| Desktop application | 167 passed; 1 optional interactive smoke skipped |
| Integration | 10 passed |
| Supabase/RLS | 414 passed after clean migration replay |
| Mobile | 114 passed; TypeScript and ESLint green |
| Worker | 4 passed; Wrangler dry-run green |
| Remaining | Desktop manual 1–10, mobile 11–12/B4, Worker deploy, update round-trip |

| Gate | Owner | State |
|---|---|---|
| Fluent UX changes merged | Engineering | Complete |
| CI at UX baseline | Engineering | Complete |
| Production Supabase project health | Engineering | Complete |
| Current client-safe API key in release workflow | Engineering | Complete |
| Local Release build/test gate | Engineering | Complete |
| v0.9.3 tag and public assets | Engineering | Complete |
| Published artifact integrity/configuration check | Engineering | Complete |
| v0.9.3 Dark main-window regression recorded | Architecture / Engineering | Complete |
| v0.9.4 foreground and native-titlebar fix | Engineering | Complete |
| v0.9.4 Light/Dark × Normal/Compact visual check | Engineering | Complete |
| v0.9.4 local Release test gate | Engineering | Complete |
| v0.9.4 tag and public assets | Engineering | Complete |
| v0.9.4 published artifact verification | Engineering | Complete |
| v0.9.5 resilient sync/border/mutex fixes | Engineering | Complete |
| v0.9.5 focused recovery/header regressions | Engineering | Complete |
| v0.9.5 production Realtime + REST live verification | Engineering | Complete |
| v0.9.5 concept main-window implementation | Engineering | Complete |
| v0.9.5 Dark/Light × Normal/Compact visual evidence | Engineering | Complete |
| v0.9.5 local Release test gate | Engineering | Complete |
| v0.9.5 candidate main CI | Engineering | Complete |
| v0.9.5 owner visual acceptance | Owner | Complete with flyout fix |
| v0.9.5 announcements flyout acceptance fix | Engineering | Complete |
| v0.9.5 tag and public assets | Engineering | Complete |
| v0.9.5 published artifact verification | Engineering | Complete |
| v0.9.6 Admin grid + Light margin fixes | Engineering | Complete |
| v0.9.6 Dark Admin + Light main visual evidence | Engineering | Complete |
| v0.9.6 local Release test gate | Engineering | Complete |
| v0.9.6 candidate main CI | Engineering | Complete |
| v0.9.6 tag and public assets | Engineering | Complete |
| Audience-aware PR #1 automated CI | Engineering | Complete |
| Audience-aware manual UX/theme acceptance | Owner | **Complete 2026-07-23 ~22:30** — final five-item re-check passed after fix round 2 (`a4bdfee`); one pre-existing timetable-editor grid quirk logged as follow-up backlog; see `docs/MANUAL-TESTS.md` final verdict |
| Audience-aware production-like migration rehearsal | Engineering | Complete — CI job `rehearsal`, run `30028621145` |
| v0.10.0 merge, tag, and public assets | Engineering | Complete — `v0.10.0` at `15ecb86`; release run `30049223114` green |
| v0.10.0 published artifact verification | Engineering | Complete — stable full/delta index, portable digest/version, cloud config, and updater target verified |
| Remaining System/150% DPI matrix | Owner / Engineering | Pending post-publication |
| v0.14.0 tag and public assets | Engineering | Complete — `v0.14.0` at `0889e44`; release run `32755130666` green |
| v0.14.0 public manifest/full/delta consistency | Engineering | Complete — hashes and byte counts verified against all uploaded assets |
| v0.13.3 → v0.14.0 pilot auto-update | Owner | Pending — real-machine offer/download/apply-on-restart evidence required |
| Win10 + Win11 full manual checklist | Owner / Engineering | Pending |

## Activity log

- 2026-08-24 — Published v0.14.0 from annotated tag `v0.14.0` at `0889e44`.
  Release run `32755130666` completed green, including all four gates and the
  2m21s publish job. The public release became Latest at 18:42:41Z and contains
  full (87,125,499 bytes), delta (9,382,428 bytes), setup, portable, and
  `releases.stable.json` assets. The manifest lists matching 0.14.0 Full and
  Delta entries with hashes and exact byte counts. Publication followed two
  distinct credential failures: the original `RELEASES_TOKEN` had expired
  (401), then its fine-grained replacement lacked contents-write permission
  (403). The corrected token published successfully. This does not close the
  real-machine v0.13.3 → v0.14.0 offer/apply-on-restart round-trip, which remains
  owner-run and open.

- 2026-08-23 — Final v0.14.0 acceptance closed. D1 passed against desktop
  `0.13.4-dev.16+00a2ab8` after PR #28 corrected inactive-user classification
  during initial organisation resolution. `MOB-F10` passed against EAS build
  `e815e646-e89c-4648-9acb-d2ce230e9396`: in-app teardown returned to role
  choice, left no files under the package data directory, and left zero package
  alarms without an app-data clear. The exact anonymous test identity and its
  device row were deleted and verified absent. `MOB-T06` remains an explicitly
  scoped pass. All acceptance gates are closed; no tag was created on Sunday,
  with v0.14.0 publication still scheduled for Monday 2026-08-24.

- 2026-08-21 — `MOB-T06` read-out is inconclusive because the evidence plan
  exceeded Android Notification history's rolling 24-hour retention. Friday's
  retained evidence is clean: exactly 12 Part-Time 2 deliveries from 17:50 to
  20:15, no morning/class-A delivery, 12 AQI Clock AlarmManager wakeups, and the
  next Part-Time 2 sets still scheduled for Monday and Tuesday. Wednesday and
  Thursday deliveries had expired from both the Settings history and
  `dumpsys notification --noredact`, so the required three-day delivery claim
  cannot be made. The Pixel was already in normal power state (`USB powered`,
  forced idle off, deep/light state active); no reset command was needed. Re-run
  with daily captures or durable app-owned delivery evidence.
- 2026-08-21 — Follow-up inspection amends, rather than replaces, that verdict
  to a scoped pass. `/proc/uptime` places an unplanned Pixel reboot at 09:36,
  which ended continuous forced Doze but also exercised the boot receiver's
  full reschedule path. The rebuilt Friday set delivered all 12 expected
  Part-Time 2 events from 17:50 through 20:15 (`12 wakes 12 alarms`) with no
  morning/class-A event; the 19 August switch-time set likewise contained 60
  class-B-band alarms and no morning alarm. The explicit residual gap is that
  19–20 August deliveries are no longer provable: a stale class-A alarm that
  fired and then self-healed would not be caught. One time-boxed `adb backup`
  attempt yielded only a 47-byte header. The Pixel's durable
  The old `notification_log` preservation requirement is moot: it held
  announcements only. v0.15.0 adds delivery and schedule-snapshot tables;
  export them before sign-out, after which privacy teardown clears them.
- 2026-08-19 — Architecture built the reflow acceptance binary
  `0.13.4-dev.9+b3454c6` (Release, 0 warnings, 0 errors) and confirmed the
  previous Release output `0.13.4-dev.7+df6bd64` predates `ed50c17`, so desktop
  rows D1-D5 had no binary to run against until now.
- 2026-08-19 — `MOB-T06` was armed and verified directly on the Pixel 9 Pro XL
  (`48231FDAS004SS`) rather than from a completion report, after an earlier
  report of the run having started could not be reproduced. `dumpsys alarm`
  showed exactly 60 pending entries for `com.mofawkes.aqiclock`, all between
  17:50 and 20:15 London time with no morning-band alarm, confirming the class
  switch reconciled. Installed version is 0.14.0 (versionCode 1). Deep idle was
  re-forced to `IDLE` with light `OVERRIDE`; `dumpsys battery unplug` was used
  and must be reset at read-out.
- 2026-08-19 — The `MOB-T06` read-out moved from Sunday 23rd to Saturday 22nd.
  The 60 alarms fall 12 per day on 19, 20, 21, 24, and 25 August: evening
  sessions do not run at weekends, so Saturday and Sunday are silent by
  timetable and carry no evidence. The observable window therefore closes after
  Friday's 20:15 event, and the verdict needs the Wednesday-to-Friday evening
  deliveries present as well as no morning-class notification. Sunday becomes
  repair slack before the Monday 24th tag.

- 2026-08-12 — Owner confirmed all Pixel 9 Pro acceptance checks passed:
  start and end-warning drift, overnight-offline delivery, three-day background
  extension, and reboot rescheduling (`MOB-T01`–`MOB-T05`). The physical timing
  and endurance gate is complete; functional/emulator mobile acceptance remains.
- 2026-08-12 — Mobile release metadata was aligned to 0.14.0. All 101 Jest
  tests, TypeScript, ESLint, Expo dependency validation, and the Android Hermes
  export passed. Android API 36 emulator preflight verified the installed
  version, declared/granted exact-alarm capability, reboot receiver, denied-
  notification launch, role chooser, and join-code entry surface. Signed-in
  and enrolled test state is still required for the full functional/Doze pass.
- 2026-08-12 — Owner enrolled the emulator as a student. Engineering passed
  class-picker validation, AM-only student setup persistence, exact-alarm
  scheduling, and denied-notification operation (`MOB-F04`, `MOB-F08`,
  `MOB-A01`, `MOB-A04`). Manual sync and student navigation remained healthy;
  Settings reported 0.14.0. Teacher/admin and forced-Doze/rescheduling rows stay
  open.
- 2026-08-12 — Owner signed the emulator in as an admin. Engineering passed
  the complete Settings row (`MOB-F17`): all three toggles persisted through a
  force-stop, warning time clamped at 0 and 15, About showed 0.14.0, and tab
  naming/navigation were correct. Original enabled/5-minute preferences were
  restored and no join code, device, account, or session was disrupted.
- 2026-08-12 — Engineering temporarily moved production Wednesday Lesson 8
  from 13:00–13:30 to 21:38–21:45, verified exact replacement alarms, and
  observed the 21:38 start and 21:40 end-warning under forced deep Doze. The
  timetable was restored to 13:00–13:30 and its next-day exact alarm was
  re-verified, completing `MOB-A02` and `MOB-A03`. The owner confirmed the
  paired Pixel also received both temporary notifications.
- 2026-08-12 — Engineering temporarily placed Wednesday Lesson 8 in an active
  21:40–21:50 window and verified the complete NOW composition, dimmed past
  rows, and active Today-row marker, passing `MOB-F02`. The period was restored
  and production re-query confirmed its original 13:00–13:30 values.
- 2026-08-12 — Engineering inspected the AQI Clock launcher icon and cold-
  launch splash at Android device size. The cream/navy quill-and-inkwell mark
  was centered, crisp, and unclipped in both masks, passing `MOB-F16`.
- 2026-08-12 — Engineering compared all seven native route hierarchies with
  `mobile/design/` using the first-run/student evidence and fresh admin tab-root
  captures. Setup had no tabs, tab roots had no hamburger/back control,
  Settings retained tabs, and Lesson 8 scrolled fully clear of the fixed status
  strip. No clipping or hierarchy defect was found, completing `MOB-F01`.
- 2026-08-12 — The dedicated active non-admin test teacher signed into mobile.
  Mobile and the owner-observed Windows client both showed no current lesson
  and Lesson 1 at 09:10 next at the same wall-clock time, matching production.
  Pull-to-refresh and Settings → Sync now completed and advanced the mobile
  timestamp, passing `MOB-F05`.
- 2026-08-12 — Engineering temporarily deactivated the dedicated test teacher.
  Mobile sync showed the explicit inactive-account state instead of an empty
  timetable, passing `MOB-F06`; the account was restored active and verified.
  The owner-provided Windows screenshot exposed a separate defect: desktop
  returns an inactive teacher to sign-in with a generic initial timetable
  download/network failure instead of explaining that the account is inactive.
- 2026-08-12 — The owner confirmed the Windows staff client exposed no admin
  tab, while mobile Teacher Settings omitted Student Devices. Engineering made
  a harmless admin-only RPC call under the staff identity against a nonexistent
  row; PostgreSQL rejected it with `42501` before mutation, passing `MOB-F15`.
- 2026-08-12 — Teacher sign-out returned mobile to role choice, but Android
  still retained the full exact lesson start/end-warning alarm set after the
  app settled. `MOB-F07` fails and remains open; release is blocked until the
  notification cleanup is fixed and re-tested. The dedicated test account
  itself remains active, and only its emulator session was signed out.
- 2026-08-12 — Engineering fixed both discovered blockers in source. Mobile
  sign-out now atomically cancels every scheduled AQI Clock notification;
  Windows initial-sync failure now maps a confirmed inactive profile to the
  inactive-account message. Mobile Jest passes 102/102, TypeScript and ESLint
  pass, and the complete .NET run passes 215 tests with 51 environment/
  interactive skips. Rebuilt APK and Windows binary acceptance remain required.
- 2026-08-12 — Engineering created five temporary audience-labelled
  announcements. Large-text emulator inspection passed the complete
  announcement presentation row (`MOB-F03`). A temporary anonymous Data API
  session verified that teachers content was excluded by RLS, and the owner
  confirmed the enrolled AM-only Pixel displayed only the everyone and AM
  controls. All temporary announcements and the anonymous test identity/device
  were deleted, and the font scale was restored. Engineering then targeted a
  temporary Wednesday control timetable to Morning Year 1–5. The owner saw it
  replace the Pixel schedule; after its targeting row was removed and the Pixel
  synced, normal lessons and the untagged break returned, and the cancelled
  22:08 control notification did not arrive. All temporary schedule rows were
  deleted and verified absent, completing `MOB-F09`.

- 2026-07-18 — Owner authorised publishing v0.9.3 with the Fluent UX changes
  made on 2026-07-17/18.
- 2026-07-18 — Engineering confirmed `main@09677a5` and its CI run are green.
- 2026-07-18 — Engineering found the packaged v0.9.2 legacy Supabase anon key
  is no longer accepted; the project itself is healthy and exposes a current
  publishable client key. The GitHub Actions variable was rotated and verified
  against the production Auth endpoint before v0.9.3 packaging.
- 2026-07-18 — Local Release gate passed with 120 tests; environment-dependent
  tests skipped as designed and will run in the tagged GitHub workflow.
- 2026-07-18 — v0.9.3 published to the public stable channel from `7ee42c2`
  after Windows and Supabase gates passed. Engineering independently verified
  the public artifact digest, version, cloud configuration, and updater target.
- 2026-07-18 — First installed v0.9.3 revealed a Dark-theme regression confined
  to the plain-WPF main window: default black foregrounds on dark surfaces and
  a native titlebar that did not follow the application theme. This is the gap
  the still-pending hands-on UI/DPI matrix was intended to catch.
- 2026-07-18 — Engineering fixed inherited main-window foregrounds and native
  titlebar theme synchronization for v0.9.4, then verified Light/Dark in Normal
  and 320×80 Compact modes plus live theme switching. No `ThemeService.Apply`
  System-resolution behavior was changed.
- 2026-07-18 — v0.9.4 published to the public stable channel from `8f98aad`
  after Windows and Supabase gates passed. Engineering independently verified
  the public artifact digest, embedded version/configuration, and stable index.
- 2026-07-18 — Clarified that the generated multi-window image is a concept,
  while v0.9.3 ships the implemented Fluent polish that preserves the accepted
  application structure.
- 2026-07-18 — Architecture visually verified the installed v0.9.4 pilot app
  post-update: dark native titlebar and readable foregrounds confirmed on the
  live machine. During verification, discovered the pre-existing
  second-instance exit crash (see Defects fixed for v0.9.5) and removed a stale
  pre-TFM-change `bin\Debug\net8.0-windows` output that shipped no longer
  existing Phase-2-era code and could mislead local testing.
- 2026-07-18 — Production logs showed Realtime WebSocket 403 responses during
  the publishable-key rotation window. A clean v0.9.4 restart at 18:49 still
  showed Offline and logged 403, despite raw query-key and fresh-user-token
  probes returning 101. The restart-only remedy was therefore rejected.
- 2026-07-18 — Engineering fixed resilient startup/retry behavior, the
  Dark-mode white border, and the second-instance APPCRASH in `75fd7d3`.
- 2026-07-18 — Engineering traced the continuing 403 to the bundled C# client
  adding the opaque publishable key as a legacy Bearer header. `ac1d1dc`
  suppresses inherited Realtime upgrade headers while preserving REST/Auth
  headers and user-JWT channel authorization. The production repaired build
  reached `Synced · just now`, logged no new Realtime error, and its second
  instance exited 0.
- 2026-07-18 — Owner made the generated concept the main-window design target
  for v0.9.5. Engineering implemented the normal/compact redesign in
  `4323d57`; Settings/Admin fine-detail alignment remains backlog.
- 2026-07-18 — v0.9.5 Release tests passed (124/124 runnable tests). Live
  screenshots confirmed 820×560 Dark/Light normal layouts, exact 320×80
  Dark/Light compact layouts, current/past/upcoming rows, empty day, and the
  announcements overlay. Tagging remains gated on owner visual acceptance.
- 2026-07-18 — Repository tracker and Fable memory were synchronized for the
  v0.9.5 candidate. Google Tasks remained unchanged because neither the Google
  Tasks connector nor a signed-in browser surface was available.
- 2026-07-18 — Candidate CI run `29654904327` passed both release-blocking
  jobs at `4cdcb54`: Windows build/tests and repeatable Supabase reset plus the
  full RLS/behaviour matrix.
- 2026-07-18 — Architecture accepted the concept redesign subject to one
  pre-tag condition: repair the announcements header collision and keep the
  overlay above the status bar. Engineering completed both in `da6983c`;
  corrected evidence is `artifacts/v0.9.5-ui/dark-announcements-fixed.png`.
- 2026-07-18 — Architecture synchronized the Google Tasks "AQI Clock" list:
  pinned candidate status, crash-fix record, v0.9.5 matrix target, and
  v0.9.2 → v0.9.5 round-trip path are current. Crash-task closure and final
  tracker sync remain post-publication actions.
- 2026-07-18 — Final candidate CI run `29659315234` passed Windows build/tests
  and the repeatable Supabase migration/RLS matrix at `95b4591`.
- 2026-07-18 — v0.9.5 was tagged at `95b4591` and published to the public
  stable channel by release run `29659428920`. Both tag-bound gates and the
  Velopack package/upload job passed.
- 2026-07-18 — Engineering independently verified the public full/delta stable
  index, portable ZIP SHA-256, embedded product version
  `0.9.5+95b4591…`, publishable-key configuration, production Supabase host,
  and updater target.
- 2026-07-18 — Owner reported WPF's default white Admin period grid in Dark
  mode and a black client-margin frame around the Light main window.
  Engineering confirmed the latter was not the native resize border: WPF-UI's
  titlebar helper had replaced the `Window.Background` resource reference.
- 2026-07-18 — Engineering fixed both defects plus Architecture's pending
  Fluent button-style corrections in `3a558e5`. Production-backed visual
  evidence is under `artifacts/v0.9.6-ui/`; the local Release gate passed
  124 runnable tests with zero failures.
- 2026-07-18 — v0.9.6 candidate CI run `29660649932` passed both required jobs
  at `58c136a`: Windows build/tests and the repeatable Supabase migration/RLS
  matrix. No release tag was created.
- 2026-07-20 — Owner chose to ship the accepted v0.9.6 surface fixes
  independently from the audience-aware rewrite. Engineering dated the
  changelog, passed main CI run `29774771246`, tagged `v0.9.6` at `d52249f`,
  and published through tag-bound release run `29775056726`.
- 2026-07-20 — Public v0.9.6 verification passed. The portable ZIP SHA-256
  is `31febd24fd533d2ddc43186d4162ed1f5f7f132ef08061081054b060ac3dd889`;
  the stable index contains matching 0.9.6 full/delta packages; the executable
  reports `0.9.6+d52249f218f82c4e652fa2361903dc1d31dce2ae`; packaged Supabase
  configuration uses HTTPS and a publishable key whose Auth settings endpoint
  returned HTTP 200; and the updater targets `MoFawkes/aqi-clock-releases`.
- 2026-07-23 — Architecture rebased `feature/audience-aware-app` onto the
  post-release `main` (docs-only divergence) and gitignored the local
  `supabase/snippets/` Studio scratch directory.
- 2026-07-23 — Architecture implemented the production-like migration
  rehearsal as CI job `rehearsal` and it passed in run `30028621145`
  alongside both existing jobs at `40624aa`. While building it, found and
  fixed frozen-migration drift: the Teacher-rename had edited the
  already-applied `20260716000300` trigger migration, so production would
  have kept the old `guard_profile_columns` wording while fresh databases
  got the new one. The frozen file is restored and the change now travels
  in the audience migration via `create or replace`. The rehearsal gate is
  Complete; the audience-aware release now waits only on the owner's manual
  UX/theme acceptance and the version-number decision.
- 2026-07-23 late evening — Second guided re-check after Sol's verified fix
  round (`23e9b5f`/`2168066`) plus live session fixes by Fable 5 (Fluent
  window full-bleed palette + titlebars, DWM caption/border colors + retry
  + backdrop strip, Graduate placeholder row disabled, sync-failure debug
  log). Result: the sync lifecycle, palette, dark Admin, scheduling/label,
  student reader filtering, System theme, and Compact all pass; five items
  remain open and block the v0.10.0 tag — theme-change border, class-B
  student period-toast delivery (regression suspect, `notification_log`
  evidence captured), teacher-role UI gating (server role verified
  correct), missing student-session exit path, and a lesson-card state
  oddity under rapid retimes. v0.10.0 is prepared (changelog `2168066`)
  but NOT tagged. Full record in `docs/MANUAL-TESTS.md`.
- 2026-07-23 ~22:30 — Owner completed the focused five-item re-check after
  `a4bdfee`. Theme switching, fresh Teacher/Admin role gating, moved-boundary
  student delivery, the four-action student tray, and lesson-card consistency
  passed; cached-admin elevation and class-A suppression retain focused unit
  coverage. Local verification passed 146 tests with 15 environment-gated
  skips, and PR run `30045041244` passed Windows, Supabase/RLS, and the
  production-like migration rehearsal. v0.10.0 is accepted for merge, tag,
  and publication. The timetable-editor period-cell commit quirk is recorded
  as non-blocking follow-up.
- 2026-07-23 23:21 — PR #1 merged to `main` as `15ecb86`, merged-main CI run
  `30048989635` passed all three jobs, and annotated tag `v0.10.0` triggered
  release run `30049223114`. Its Windows, Supabase/RLS, production-like
  rehearsal, package, artifact-upload, and public Velopack publication jobs
  all passed. Public release `AQI Clock 0.10.0` is live with full, delta,
  Setup, portable, and stable-index assets. The portable SHA-256 is
  `de8eacfce6631a376a237164539b1a5fdd55206f6fe149e9ff442708c07bc625`;
  the executable reports `0.10.0+15ecb86d6763255f78f287301e400debced2132d`;
  packaged Supabase configuration uses HTTPS plus a publishable key, and the
  updater targets `MoFawkes/aqi-clock-releases`.
- 2026-07-23 — Owner ran the audience-aware click-through live against a
  local stack, guided by Fable 5. Functional sections largely passed:
  sign-in fork, student sessions with class/Naseehah filtering (all five
  audience-visibility outcomes correct, scheduled announcement arrived on
  time, toast routing correct, restart wipe correct), class management, and
  the full announcements suite. Two defects were diagnosed, fixed, and
  re-verified during the session (DataGrid edits never committed on Save;
  Classes-tab error message had no visible element). Three merge-blockers
  remain for engineering: dead sync after sign-out/sign-in cycles, the
  unapplied Navy/Cream palette on FluentWindows plus the Dark frame ring,
  and the invisible unknown-class tag error. Full pass/fail record and
  owner-ratification observations are in `docs/MANUAL-TESTS.md`.

## Handoff rules

1. Update the relevant Architecture or Engineering section before ending a
   work session that changes scope or release state.
2. Add a dated activity-log entry for decisions, releases, incidents, and
   resolved blockers.
3. Never place passwords, Supabase secret/service-role keys, release tokens,
   or other credentials in this file.
4. Link detailed acceptance evidence from this file rather than duplicating
   long logs.
5. After executing an Architecture plan, Engineering must finish with a full
   report for Fable 5 covering: delivered scope, files and commits, automated
   and visual verification, release/deployment state, tracker synchronization,
   unresolved risks, and the exact next acceptance actions.
