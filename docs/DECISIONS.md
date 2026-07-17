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

## Open items awaiting owner input (not architectural blockers)
See SPECIFICATION.md §5 (B-1 … B-8): timezone, school week, default warning minutes, account model, audit retention, Supabase tier, update hosting, branding.
