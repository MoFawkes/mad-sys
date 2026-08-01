# Architectural decisions

Living document. New decisions are appended; superseded ones are marked, never deleted. ADR-002 onward use a condensed format (decision + rationale + rejected alternatives).

## ADR-001: Bootstrap without the referenced architecture documents

- **Status:** Accepted for Phase 1 only
- **Date:** 2026-07-15

### Context

The repository was empty at the beginning of Phase 1: `docs/`, `TASKS.md`, and `README.md` were absent, and Git had no commits. Consequently, the referenced approved product and architecture documents could not be reviewed.

### Decision

Create only the explicitly requested foundation using the required project structure. Use .NET 8 because it is installed and supported through November 2026. Keep dependencies directed inward: App → Application/Infrastructure, Infrastructure → Application/Domain, and Application → Domain. Defer all domain behavior and external-service implementations until approved specifications are available.

Use the shared `Microsoft.AspNetCore.App` framework for Microsoft dependency injection, configuration, options validation, and structured logging in Phase 1. This avoids adding production NuGet dependencies before the architecture documents can be reviewed. Test-only packages remain normal NuGet references.

A repository-local `NuGet.Config` explicitly selects NuGet.org so builds do not depend on inaccessible or machine-specific user-profile configuration.

### Consequences

The foundation is intentionally minimal. Supabase, Realtime, SQLite, notifications, offline behavior, and the timetable engine are not implemented. Future work must re-evaluate this decision against the approved documents before Phase 2.

*Phase 2 note (2026-07-15): the architecture documents now exist (docs/). The Phase 1 layering is confirmed and kept. The `Microsoft.AspNetCore.App` framework reference is to be replaced with `Microsoft.Extensions.Hosting` NuGet packages plus Serilog when Phase 2 begins — a desktop app should not carry the ASP.NET Core shared framework.*

## ADR-016: Target Windows 10 build 17763 or later for native toast activation
**Accepted** 2026-07-16.
`AqiClock.App` and its UI test project target `net8.0-windows10.0.17763.0`. This selects the desktop compatibility surface in `Microsoft.Toolkit.Uwp.Notifications` instead of its platform-neutral build, which does not expose unpackaged toast activation. Windows 10 1809 is within the product's Windows 10/11 scope; the Domain, Application, Infrastructure, and non-UI tests remain platform-neutral `net8.0`.

## ADR-017: Public releases-only repository with release-scoped CI credential
**Accepted** 2026-07-16.
Velopack assets are hosted in the public `MoFawkes/aqi-clock-releases` repository so installed clients can update anonymously while the source repository remains private. GitHub's built-in `GITHUB_TOKEN` is scoped to the source repository and cannot publish cross-repository; therefore the release workflow requires a fine-grained `RELEASES_TOKEN` Actions secret with contents-write access to that repository only. The credential exists solely in CI and never enters source, artifacts, configuration, or the client. Rejected: embedding a token in the client, making `mad-sys` public, or silently claiming cross-repository publication works without credentials.

## ADR-018: Native password recovery uses an AQI Clock protocol activation
**Accepted** 2026-07-17.
Supabase invitation/recovery links must return to a password-setting surface; the hosted project initially redirected to the unusable default `localhost:3000`. Packaged AQI Clock registers `aqiclock://reset-password` under the current user's URL protocols through Velopack install/update hooks and removes it on uninstall. Supabase redirects the short-lived recovery session to that URI; the app validates the exact scheme, host, and `type=recovery`, updates the password through the Auth API, revokes the temporary session, and never persists or logs the token. A current-user-only named pipe forwards recovery activation to an already-running single instance. Rejected: a public web recovery page (additional hosting/security surface), direct edits to `auth.users`, and passing a service-role key to the client.

## ADR-019: WPF-UI Fluent presentation with a compact-window exemption
**Accepted** 2026-07-17.
Adopt WPF-UI 4.3.0 for the desktop presentation layer, Fluent controls, theme resources, navy application accent, and Mica-capable window chrome. Sign-in, password recovery, Settings, and Admin use `FluentWindow`; MainWindow intentionally remains a plain WPF `Window` because its accepted 320×80 compact mode changes `WindowStyle` at runtime and must stay frameless, draggable, and independently persisted. Existing dynamic brush keys remain compatibility aliases while app-owned light/dark dictionaries are swapped without clearing WPF-UI's merged dictionaries. Any Fluent-window or control conversion that affects a rendered-window test must update that strict binding test in the same commit; framework template binding errors are fixed rather than filtered. PerMonitorV2 is declared in the application manifest and is manually validated at 100% and 150% scale before release. Rejected: retaining default WPF styling, replacing the approved MVVM/window architecture, or applying Fluent chrome to MainWindow at the cost of compact-mode stability.

---

## ADR-002: Stay on .NET 8 for MVP; CommunityToolkit.Mvvm + Generic Host
**Accepted** 2026-07-15.
.NET 8 is what Phase 1 scaffolded, what is installed (SDK 8.0.407), and is LTS to Nov 2026 — sufficient for MVP delivery and pilot. An upgrade to the then-current LTS is a **required pre-wide-rollout task** (TASKS.md Phase 8). MVVM via CommunityToolkit.Mvvm (source generators, no framework lock-in) over Prism/ReactiveUI; Generic Host provides DI, hosted services, and options binding with zero custom infrastructure. Rejected: upgrading to a newer .NET now (churn while the installed toolchain already builds green).

## ADR-003: Single-org product on a multi-org-ready schema
**Accepted** 2026-07-15.
Every server table carries `org_id` and all RLS scopes by it, but the app assumes exactly one organisation (no org picker; the SQLite cache drops `org_id`). Cost today: one column and one helper function. Benefit: future multi-tenancy needs no data migration. Rejected: full multi-tenant UI now (over-engineering) and omitting `org_id` (painful retrofit).

## ADR-004: Supabase is the only backend; no custom API server
**Accepted** 2026-07-15.
Clients talk directly to Supabase (PostgREST + Realtime + Auth) with RLS as the authorisation layer. A middle-tier API adds hosting, auth plumbing, and latency for zero MVP benefit at this scale. Consequence: server-critical business rules live in SQL — triggers/constraints implement the last-admin guard, profile column guards, and audit capture.

## ADR-005: Raw SQL for SQLite (no EF Core / ORM)
**Accepted** 2026-07-15.
The cache is 11 small tables with snapshot-replace writes and simple reads. Microsoft.Data.Sqlite + hand-written SQL + a ~50-line migration runner is less code than EF Core configuration, starts faster, and avoids model drift between two databases. Rejected: EF Core (overkill), sqlite-net (weaker typing).

## ADR-006: Period times are local wall-clock `time` values; org timezone is informational
**Accepted** 2026-07-15.
School life runs on the wall clock: "Period 1 at 08:30" means 08:30 whatever DST does. Storing UTC instants would shift lessons by an hour across DST transitions — actively wrong. Consequence: the schedule engine computes with local `DateTime` and handles DST by recomputation, never duration arithmetic across transitions (ARCHITECTURE.md §4). `organizations.timezone` exists for future cross-timezone viewing; MVP assumes machines are in the school's timezone.

## ADR-007: Editing is online-only; offline is strictly read-only
**Accepted** 2026-07-15. **The most consequential simplification in the design.**
No offline write queue, no client-side merge, no vector clocks. Admins edit a shared operational document — offline queueing would risk silently resurrecting stale timetables hours later, which is worse than "you need internet to edit". Consequence: conflict handling collapses to row-level last-write-wins plus a courtesy "changed underneath you" prompt (ARCHITECTURE.md §6). Revisit only if admins demonstrably need offline editing.

## ADR-008: Sync is a full snapshot pull per table; Realtime events are signals, not data
**Accepted** 2026-07-15.
The whole dataset is kilobytes. Re-pulling a table on any of its change events (500 ms debounce) is trivially cheap and self-healing against missed, duplicated, or out-of-order Realtime events. Rejected: delta sync with tombstones and cursors — a classic over-engineering trap at this scale. Revisit if any table exceeds ~5k rows (only plausible candidate: a future per-user timetable feature).

## ADR-009: In-process notification scheduling with an SQLite dedup log; no OS-scheduled toasts
**Accepted** 2026-07-15.
Toasts pre-registered with Windows cannot be reliably rebuilt when an admin edits the timetable mid-day and give no dedup across restarts. The app is tray-resident with auto-start, so in-process firing from the 1 s tick is dependable; the persisted `notification_log` guarantees at-most-once per event; a 120 s grace window governs late firing after sleep/restart (ARCHITECTURE.md §7). Trade-off accepted: no notifications when the app is not running.

## ADR-010: Velopack for packaging and auto-update; per-user install; MSIX rejected
**Accepted** 2026-07-15.
Velopack: no admin rights, delta updates, works with plain GitHub Releases, and creates the Start-menu shortcut that toast notifications require. MSIX rejected: mandatory signing/store friction, clumsier auto-start story, worse fit for an IT-light school. A code-signing certificate is required before wide rollout but not for the pilot (SECURITY.md §5).

## ADR-011: Test pyramid — exhaustive unit tests on the schedule engine; RLS integration tests in CI; manual checklist for OS-integration UI
**Accepted** 2026-07-15.
All timetable/notification edge cases are pure functions over `IClock` — cheap to test exhaustively, and that is where correctness lives. RLS misconfiguration is the top security risk, so per-role allow/deny tests against a CLI-launched local Supabase are release-blocking. Automated Windows UI testing (toasts, tray, sleep/resume) is high-cost/low-yield → scripted manual checklist. Rejected: Appium/WinAppDriver E2E in MVP.

## ADR-012: Invite-only accounts; first admin bootstrapped via the Supabase dashboard
**Accepted** 2026-07-15.
Open self-signup would let anyone with the app binary join the organisation. MVP user creation happens in the Supabase dashboard (minutes of admin work per hire — acceptable at school scale); in-app invitations are post-MVP. The first admin's role is set once via the seed script/dashboard.

## ADR-013: Announcement read-state and user settings are local per machine, not synced
**Accepted** 2026-07-15.
Syncing read receipts and preferences adds tables, policies, and sync paths for negligible MVP value (staff typically use one machine). Revisit with the synced-preferences post-MVP item.

## ADR-014: Hard deletes with FK RESTRICT guards + audit before-images (no soft-delete columns)
**Accepted** 2026-07-15.
Soft deletes complicate every query and the cache. Instead: deleting a timetable referenced by the week schedule or an override is blocked (RESTRICT), forcing explicit reassignment; `is_archived` covers "hide but keep"; the audit log preserves deleted rows' content for history.

## ADR-015: Permit plaintext Supabase transport only for loopback development
**Accepted** 2026-07-16.
Production Supabase endpoints must use HTTPS/WSS as required by SECURITY.md §5. The gateway permits `http://` only when `Uri.IsLoopback` is true so the official `supabase start` stack at `127.0.0.1` can support integration tests. Non-loopback plaintext endpoints fail during gateway construction. This is a narrow development exception, not a relaxation of production transport security.

---

## ADR-020: Personal student audience on mobile
**Accepted** 2026-07-28.
The mobile student session persists its selected classes and independent Naseehah AM/PM choices locally. Owner decision 2026-08-01 supersedes the former shared-PC desktop clause: desktop student installations are also one student per machine and persist the same choices in the local cache. The mobile clock is also a personal timetable: periods tagged only for other classes are omitted from its current, next, and daily views. Untagged periods are school-wide and always remain visible, covering breaks, assemblies, Jumu'ah, and installations whose optional class tagging is incomplete. Notifications use the same predicate. Desktop continues to display the full school day and applies class selection only to notifications. Rejected: retaining per-launch selection on personal desktop installs, and treating untagged periods as matching no students.

## ADR-021: Reconcile OS-scheduled notifications on mobile
**Accepted** 2026-07-28.
The mobile client derives a desired set of future lesson notifications from its SQLite snapshot and reconciles that set with notifications scheduled by the operating system. Event keys are used as stable identifiers, so timetable edits cancel or move stale requests without creating duplicates. Reconciliation runs after relevant syncs, on foreground, after audience or notification-setting changes, and through a best-effort six-hour background task. The seven-day horizon is sorted and capped at 60 requests to stay below iOS's 64-pending-notification limit. This supersedes ADR-009 on mobile only: the tray-resident Windows client keeps its one-second tick and SQLite firing log. Android delivery is intentionally inexact because AQI Clock is not eligible for Play-restricted exact-alarm permissions; measured Android 13+ drift remains a release gate, with server push deferred unless that drift is unacceptable.

## ADR-022: Keep the Expo companion in this repository
**Accepted** 2026-07-28.
The Expo SDK 54 application lives under `mobile/` beside the Windows client, Supabase migrations, and shared documentation. The schedule engine is a pure TypeScript semantic port with case-for-case tests; platform I/O stays in data, notification, and UI layers. One repository keeps backend-policy changes, desktop compatibility, mobile behavior, and release gates reviewable together. The mobile CI job is additive and does not path-filter the existing required checks. Rejected: a separate repository that could deploy against incompatible migrations, and sharing runtime code across C# and TypeScript through generated bindings.

## ADR-023: Enrol anonymous student devices with server-filtered announcements
**Accepted** 2026-07-28.
Personal student phones and desktop student installations receive anonymous Supabase Auth identities and join one organisation through a long random code and the `enroll_student_device` SECURITY DEFINER RPC. Anonymous identities never receive profiles; student RLS is additive, read-only, excludes staff/audit data, and removes confidential announcement audiences server-side. Both clients persist the rotated session and refresh it under application control. GoTrue requires global signup for anonymous creation, so a fail-closed Before User Created hook rejects public non-anonymous signup while Admin API teacher provisioning continues. This is weaker than a disabled global flag if the hook is absent, so the migration, hosted hook configuration, and signup matrix are release gates. Rejected: embedding teacher credentials, exposing broad unauthenticated reads, or relying solely on client-side announcement filtering.

Owner decision 2026-08-01: the desktop QoL work is release-coupled to v0.11.0 because its enrolment path depends on the same RPC and student RLS migrations. It may merge with v0.11.0 or follow deployment of that backend, but cannot ship first as a standalone desktop release.

## ADR-024: Distribute student join codes through an isolated secret table
**Accepted** 2026-07-28.
Student join codes live in `organization_join_codes`, an RLS-enabled table with no Data API grants or policies. Only admin-gated SECURITY DEFINER RPCs can reveal or mutate them. This preserves the shipped desktop client's `organizations?select=*` sync: column-level grants were rejected because PostgreSQL then refuses wildcard selects. A trigger assigns a code after every organisation insert without granting callers access to the generator. The Windows admin workspace displays and QR-encodes the code; verified mobile admins may share it but cannot rotate or revoke from the phone. Rotation stops future enrolments while existing devices continue; revocation separately removes every enrolled device. Neither operation writes the code to audit history. Rejected: keeping the secret on `organizations`, column grants that break deployed clients, and combining routine rotation with destructive device revocation.

## ADR-025: Restore dialog placement against the intended monitor
**Accepted** 2026-08-01.
Admin and Settings leave their XAML `CenterOwner` startup location untouched until a saved placement exists. Saved rectangles select the nearest live monitor through `MonitorFromRect`, so an unplugged display falls back safely, and `GetMonitorInfo` supplies that monitor's work area. Because Win32 reports the work area in physical pixels while WPF placement uses device-independent units, the controller uses `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` for the selected target and converts its work rectangle before applying the shared pure clamp. No Windows Forms dependency is introduced. Monitor selection and mixed-DPI transitions remain manual acceptance items because display topology cannot be represented by the unit-test environment; the clamp arithmetic and no-saved-placement behavior remain automated.

## Open items awaiting owner input (not architectural blockers)
See SPECIFICATION.md §5 (B-1 … B-8): timezone, school week, default warning minutes, account model, audit retention, Supabase tier, update hosting, branding.
