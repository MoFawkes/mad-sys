# AQI Clock — Architecture / Engineering Status

Last updated: 2026-07-18 17:31 BST

This is the shared handoff document for Fable 5 (Architecture) and Codex
(Implementation / Engineering). Keep it current when scope, release state,
risks, or ownership changes. `TASKS.md` remains the delivery checklist,
`CHANGELOG.md` remains the release record, and `docs/MANUAL-TESTS.md` remains
the acceptance script.

## Live state

| Area | State |
|---|---|
| Staff pilot | v0.9.2 installed on 3 staff machines |
| Public release channel | v0.9.3 is live on `MoFawkes/aqi-clock-releases` |
| Source | `main`; v0.9.4 dark-mode fix in release preparation |
| Production backend | Supabase project active and healthy |
| Latest release | v0.9.3 — Fluent UX polish from 2026-07-17/18 |
| Release candidate | v0.9.4 — main-window Dark-theme regression fix |

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
  concept, not the v0.9.3 acceptance baseline. Matching it would be a separate
  architecture/design and implementation scope.

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

Next architecture inputs:

- Record any post-v0.9.3 design/functionality notes as explicit backlog items
  with acceptance criteria.
- Decide whether the generated concept image becomes a future redesign target.
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

In progress / next:

- Publish and verify v0.9.4 through the tag-bound release workflow.
- Finish the hands-on System and 150% DPI portions of the UI/DPI matrix.
- Perform the v0.9.2 → v0.9.4 auto-update check on a pilot machine.
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
| v0.9.4 tag and public assets | Engineering | In progress |
| Remaining System/150% DPI matrix | Owner / Engineering | Pending post-publication |
| v0.9.2 → v0.9.4 pilot auto-update | Owner / Engineering | Pending |
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
- 2026-07-18 — Clarified that the generated multi-window image is a concept,
  while v0.9.3 ships the implemented Fluent polish that preserves the accepted
  application structure.

## Handoff rules

1. Update the relevant Architecture or Engineering section before ending a
   work session that changes scope or release state.
2. Add a dated activity-log entry for decisions, releases, incidents, and
   resolved blockers.
3. Never place passwords, Supabase secret/service-role keys, release tokens,
   or other credentials in this file.
4. Link detailed acceptance evidence from this file rather than duplicating
   long logs.
