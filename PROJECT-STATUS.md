# AQI Clock — Architecture / Engineering Status

Last updated: 2026-07-18 19:01 BST

This is the shared handoff document for Fable 5 (Architecture) and Codex
(Implementation / Engineering). Keep it current when scope, release state,
risks, or ownership changes. `TASKS.md` remains the delivery checklist,
`CHANGELOG.md` remains the release record, and `docs/MANUAL-TESTS.md` remains
the acceptance script.

## Live state

| Area | State |
|---|---|
| Staff pilot | v0.9.2 installed on 3 staff machines |
| Public release channel | v0.9.4 is live on `MoFawkes/aqi-clock-releases` |
| Source | `main`; v0.9.5 candidate at `4cdcb54`, untagged |
| Production backend | Supabase project active and healthy |
| Latest release | v0.9.4 — main-window Dark-theme regression fix |
| Next release | v0.9.5 — resilient sync, border/crash fixes, concept main-window redesign |
| Candidate CI | Green at `4cdcb54` (run `29654904327`) |
| Release workflow | Last tag-bound run green at `8f98aad` |

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
- Settings/Admin fine-detail alignment to the concept remains follow-up
  backlog and is not part of v0.9.5.

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

Next architecture inputs:

- Record any post-v0.9.3 design/functionality notes as explicit backlog items
  with acceptance criteria.
- Accept or reject the v0.9.5 main-window screenshots against the approved
  concept before Engineering creates the release tag.
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

In progress / next:

- Owner visual acceptance of the v0.9.5 main window against the concept.
- After acceptance, tag and publish v0.9.5 through the established tag-bound
  Windows and Supabase gates.
- Retry the Google Tasks "AQI Clock" list synchronization when its connector or
  a signed-in browser surface is available; neither was exposed in this
  Engineering session.
- Finish the hands-on System and 150% DPI portions of the UI/DPI matrix.
- Perform the v0.9.2 → v0.9.5 auto-update check on a pilot machine.
- Complete the installer/update/uninstaller round trip and record results in
  `docs/MANUAL-TESTS.md`.

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
| v0.9.5 owner visual acceptance | Owner | Pending |
| v0.9.5 tag and public assets | Engineering | Blocked on owner acceptance |
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
