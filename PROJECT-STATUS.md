# AQI Clock — Architecture / Engineering Status

Last updated: 2026-07-18 16:54 BST

This is the shared handoff document for Fable 5 (Architecture) and Codex
(Implementation / Engineering). Keep it current when scope, release state,
risks, or ownership changes. `TASKS.md` remains the delivery checklist,
`CHANGELOG.md` remains the release record, and `docs/MANUAL-TESTS.md` remains
the acceptance script.

## Live state

| Area | State |
|---|---|
| Staff pilot | v0.9.2 installed on 3 staff machines |
| Public release channel | `MoFawkes/aqi-clock-releases`, stable channel |
| Source | `main`; Fluent UX baseline commit `09677a5` |
| Production backend | Supabase project active and healthy |
| Release in progress | v0.9.3 — Fluent UX polish from 2026-07-17/18 |
| CI baseline | Green at `09677a5` |

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

Next architecture inputs:

- Record any post-v0.9.3 design/functionality notes as explicit backlog items
  with acceptance criteria.
- Decide whether the generated concept image becomes a future redesign target.
- Keep code signing, Supabase plan review, and .NET LTS migration in the
  pre-wide-rollout gate.

## Implementation / Engineering — Codex

In progress:

- Prepare and publish v0.9.3 from the Fluent UX baseline.
- Run automated build/test gates and monitor the tagged release workflow.
- Verify the public Velopack assets after publication.

Completed during release preparation:

- Replaced the invalid legacy Supabase client key in GitHub Actions with the
  current client-safe publishable key and verified the Auth endpoint accepts it.
- Passed the local Release test gate: 120 tests passed; the 14 local-stack
  Supabase tests and one credential-dependent smoke test skipped as designed.

Post-publication:

- Run the hands-on Light/Dark/System × Normal/Compact × 100/150% DPI matrix.
- Perform the v0.9.2 → v0.9.3 auto-update check on a pilot machine.
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
| v0.9.3 tag and public assets | Engineering | In progress |
| UI/DPI hands-on matrix | Owner / Engineering | Pending post-publication |
| v0.9.2 → v0.9.3 pilot auto-update | Owner / Engineering | Pending |
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
