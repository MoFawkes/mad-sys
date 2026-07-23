# AQI Clock — Architecture / Engineering Status

Last updated: 2026-07-23 18:45 BST

This is the shared handoff document for Fable 5 (Architecture) and Codex
(Implementation / Engineering). Keep it current when scope, release state,
risks, or ownership changes. `TASKS.md` remains the delivery checklist,
`CHANGELOG.md` remains the release record, and `docs/MANUAL-TESTS.md` remains
the acceptance script.

## Live state

| Area | State |
|---|---|
| Staff pilot | v0.9.2 installed on 3 staff machines |
| Public release channel | v0.9.6 is live on `MoFawkes/aqi-clock-releases` |
| Source | `main`; v0.9.6 tagged at `d52249f` |
| Production backend | Supabase project active and healthy |
| Latest release | v0.9.6 — Light/Dark surface consistency fixes |
| Next release | Audience-aware sign-in and announcements from draft PR #1; version TBD |
| Candidate CI | PR #1 green at `40624aa` (run `30028621145`), including the new migration-rehearsal job |
| Release workflow | v0.9.6 tag-bound run `29775056726` green |

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

- Architecture: close the crash task and perform the post-release Google Tasks
  synchronization now that v0.9.5 is public. The pre-release pinned status,
  matrix, crash, and round-trip task updates were already complete.
- Finish the hands-on System and 150% DPI portions of the UI/DPI matrix.
- Perform the v0.9.2 → v0.9.5 auto-update check on a pilot machine.
- Complete the installer/update/uninstaller round trip and record results in
  `docs/MANUAL-TESTS.md`.
- Require green candidate CI and owner acceptance before merging and tagging
  the audience-aware release.
- Owner decision 2026-07-20: v0.9.6 ships the already-accepted surface fixes
  independently. The audience-aware rewrite ships as a later version after
  its manual UX/theme checklist and production-like migration rehearsal pass.
- With the rehearsal now green in CI, the audience-aware release is blocked
  only on the owner's manual UX/theme acceptance in `docs/MANUAL-TESTS.md`
  and the version-number choice for the release itself.
- 2026-07-23 owner click-through outcome: engineering fix round required
  before re-acceptance — (1) sign-out/sign-in leaves sync permanently dead
  (`SyncService` start-once guard + no stop on sign-out); (2) the new
  Navy/Cream palette does not render on the FluentWindows and the Dark
  main-window frame ring is back; (3) the period-tags unknown-class error
  is invisible. Full record in `docs/MANUAL-TESTS.md` §"2026-07-23 session
  results". Two grid defects were fixed and re-verified live during the
  session.

## Release gates

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
| Remaining System/150% DPI matrix | Owner / Engineering | Pending post-publication |
| v0.9.2 → v0.9.5 pilot auto-update | Owner / Engineering | Pending |
| Win10 + Win11 full manual checklist | Owner / Engineering | Pending |

## Activity log

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
