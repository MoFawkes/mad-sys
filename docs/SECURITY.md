# AQI Clock — Security Design

Status: Draft 1.0 (planning) · Last updated: 2026-07-15

Threat model: a small-school internal tool. Primary risks are (1) staff modifying timetable data they shouldn't, (2) leaked credentials/keys, (3) a stolen/shared laptop exposing a session. Not in scope: nation-state attackers, malicious admins (they legitimately hold write access; audit log is the control).

---

## 1. Authentication

- Supabase Auth, **email + password** only in MVP (no OAuth — school staff may not have Google/Microsoft org accounts; revisit post-MVP).
- Invite-only: signups disabled in Supabase Auth settings; admins create users in the Supabase dashboard (MVP) which fires the invite/confirmation email.
- Password policy: Supabase minimum length set to 10; leaked-password protection enabled.
- Password reset: standard Supabase email flow, initiated from the sign-in screen.
- The app ships only the **anon key** + project URL (safe to embed by design — all authority comes from RLS + the user's JWT). The **service-role key is never** in the client, the repo, or CI variables accessible to the build; it is used only by humans/migration tooling.

## 2. Authorisation model

- Two roles, `staff` and `admin`, stored in `profiles.role` (Postgres), never in client-editable storage and never trusted from the client.
- UI hides admin surfaces based on the cached profile, but this is cosmetic; **every** privilege boundary is enforced by RLS server-side.
- Last-admin protection: a trigger rejects a role change or deactivation that would leave the org with zero active admins.

## 3. Row Level Security policies (normative)

Helper functions (SECURITY DEFINER, `stable`):
```sql
current_org_id() → (select org_id from profiles where id = auth.uid() and is_active)
is_admin()       → exists(select 1 from profiles where id = auth.uid() and role='admin' and is_active)
```

| Table | SELECT | INSERT / UPDATE / DELETE |
|---|---|---|
| organizations | org members (id = current_org_id()) | none from client (dashboard only) |
| profiles | org members | UPDATE own row, `display_name` only (column-guard trigger); admin may update role/is_active/display_name of org rows; no client INSERT/DELETE (auth trigger creates rows) |
| timetables, periods, week_schedule, date_overrides, announcements | org members (org_id = current_org_id(); periods via their timetable's org) | admin only, and `is_active` must be true; new rows forced to `org_id = current_org_id()` via WITH CHECK |
| audit_log | admin only | **no policies** — writes happen inside SECURITY DEFINER trigger function only |

Additional guards:
- Deactivated users (`is_active = false`): `current_org_id()` returns null → every policy fails → instant lockout even with a valid unexpired JWT.
- All policies use `WITH CHECK` mirrors of their `USING` clauses so rows cannot be moved across orgs.
- CHECK constraints (times, text lengths, role values) back up client validation.
- Realtime: authorization enabled so change events respect RLS.
- RLS tests are release-blocking in CI (ARCHITECTURE.md §9): for each table × role × (own org / other org) assert allow/deny.

Hardening details implemented by the migrations:
- All `SECURITY DEFINER` helpers live in the unexposed `private` schema and use `set search_path = ''`; names in policies are fully qualified.
- Default function execution is revoked. Only `authenticated` receives `USAGE` on `private` and `EXECUTE` on the two RLS lookup helpers; trigger functions are not client-callable.
- Data API table privileges are explicit: `anon` receives none, while `authenticated` receives only the operations for which an RLS policy exists. RLS remains the row-level authority.
- RLS helper calls are wrapped in scalar `select` expressions so Postgres can cache them per statement.

## 4. Client-side storage security

| Data | Location | Protection |
|---|---|---|
| Supabase session (access + refresh token) | `%LOCALAPPDATA%\AqiClock\session.bin` | Encrypted with **DPAPI, CurrentUser scope** — unreadable by other Windows accounts |
| SQLite cache | `cache.db` | Not encrypted (timetables/announcements are low-sensitivity); contains no credentials. Accepted risk, revisit if sensitive data is ever cached |
| Settings | `settings.json` | Plain JSON, nothing sensitive permitted in it |
| Logs | `logs\` | Serilog scrubbing rule: never log tokens, passwords, or full JWTs; 7-day rolling retention |

Sign-out wipes `session.bin`, `cache.db`, and `announcement_read`/`notification_log` (shared-machine hygiene, UI-FLOWS.md J8).

## 5. Transport and update security

- All Supabase traffic is HTTPS/WSS (client refuses plain HTTP endpoints).
- Updates: Velopack packages are fetched over HTTPS from the release host; releases are checksummed by Velopack. Code signing (OV cert) is a **pre-rollout requirement** — until then, first-install SmartScreen warnings are documented for IT.
- Dependencies: NuGet lock files committed; Dependabot/`dotnet list package --vulnerable` in CI.

## 6. Auditability

- Server-side trigger-based audit (DATABASE.md §1.8): tamper-resistant from the client, captures actor from `auth.uid()`, before/after images, server timestamps.
- Audit rows are append-only; no UPDATE/DELETE grants or policies exist for any role including admin (retention/pruning would be a dashboard/service-role operation, B-5).

## 7. Privacy

- Personal data held: staff name + email + role. No student data anywhere in the system (worth keeping true — it keeps the compliance surface minimal).
- Data residency: choose the Supabase region nearest the school at project creation (owner input at setup; default `eu-west-2`).
