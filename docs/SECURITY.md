# AQI Clock — Security Design

Status: Implemented · Last updated: 2026-07-28

The primary risks are unauthorised timetable changes, staff-directory disclosure to student devices, leaked credentials or keys, and retained sessions on lost/shared devices. RLS is the authorisation boundary; clients are not trusted to enforce confidentiality.

## Authentication and roles

Teachers and administrators use invited email/password accounts. Profiles store `teacher`, `admin`, or reserved `graduate`; roles are not trusted from JWT user metadata. A restored cached admin is treated as teacher-level until a fresh profiles snapshot confirms it. A successful profiles pull with no own row produces the explicit inactive-account state.

Public email signup remains blocked even though GoTrue's global signup flag must be enabled for anonymous sign-in. The `private.before_user_created` Auth hook allows only payloads positively identified as anonymous and fails closed otherwise. Admin API creation bypasses the hook and remains the teacher-account provisioning path. The hook and signup matrix are release-gating and must also be configured in the hosted Auth dashboard.

Mobile students use `signInAnonymously()` and enrol with a 16-character organisation join code through `public.enroll_student_device`. They have an Auth identity but no profile. The desktop Student mode remains different: it is a device-local audience over a cache previously populated by a teacher on that shared PC.

The join code is a distributable shared secret stored in the ungranted `organization_join_codes` table. Admin-gated RPCs reveal or rotate it; no persona can read the table through PostgREST. Rotation blocks future use of the old code without removing already-enrolled phones. The separate revocation RPC deletes all device enrolments and is the response to disclosure. Audit rows record rotation time or revoked-device count, never the credential.

## RLS boundaries

- Active teachers read their organisation; administrators alone receive normal data write policies.
- Student devices resolve their organisation through their own `student_devices` row.
- Student devices can read schedules, classes, and eligible announcements, but never `profiles` or `audit_log`.
- Student announcement RLS removes drafts, deleted/future rows, and teacher/graduate audiences before data reaches the phone.
- Client predicates still enforce publication/expiry and the selected AM/PM/class audience. They remain required for teachers, whose broader organisation policy is unchanged.
- Devices receive no write policy. Existing admin predicates fail because anonymous identities have no profile.
- `student_devices` is not in the Realtime publication.

All `SECURITY DEFINER` helpers set an empty `search_path`, use explicit schema names, and have narrowly assigned execute grants. The public enrolment RPC verifies both `auth.uid()` and the anonymous JWT claim.

## Client secrets and storage

Only the project URL and a client-safe publishable key (`sb_publishable_*`) belong in either client. A service-role or secret key must never enter source, EAS variables, binaries, logs, or device storage. No code assumes the publishable key has JWT shape.

Windows persists the staff session with DPAPI. Mobile uses an atomic generation-switched, chunked Expo SecureStore adapter because a Supabase session exceeds SecureStore's per-value limit. Mobile SQLite contains low-sensitivity timetable and announcement data but no credentials.

Mobile teacher sign-out and End student session both cancel pending lesson notifications, sign out, clear preferences, and wipe SQLite. Windows teacher sign-out retains its shared reference cache specifically so the shared-PC Student mode can continue offline; it still removes the credential session.

Recovery uses distinct schemes: `aqiclock://reset-password` on desktop and `aqiclock-mobile://reset-password` on mobile. Both exact redirects must be allow-listed in hosted Supabase Auth. Recovery tokens are handled in memory and never logged.

## Transport, offline use, and privacy

Production endpoints require HTTPS/WSS. Loopback HTTP is permitted only for the local Supabase test stack.

Offline mode is read-only. The UI reads SQLite indefinitely and shows the last complete sync time, escalating after seven days. OS-scheduled mobile lesson notifications continue from the cached plan while offline.

The service stores staff identity and role plus anonymous device identifiers and local class preferences. It does not store student names, email addresses, attendance, or other personal student records.

## Known release risks

- `student_devices.last_seen_at` changes only during enrolment/re-enrolment and is not yet a reliable liveness signal.
- Anonymous Auth users accumulate until a cleanup policy is implemented.
- Hosted Auth hook, anonymous-signin toggle, signup toggle, and both recovery redirects require dashboard configuration as well as repository configuration.
- Physical Android 13+ notification drift remains a release-blocking measurement; the app intentionally requests no restricted exact-alarm permission.
- Supabase tier/realtime-message volume must be reviewed before wide mobile rollout.
