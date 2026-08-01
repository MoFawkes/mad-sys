# AQI Clock — Database Design

Status: Implemented · Last updated: 2026-07-28

Supabase Postgres is the source of truth. The Windows and Expo clients keep disposable SQLite read caches; neither client renders a network response directly.

## Supabase schema

All organisation-owned tables use UUID keys and server timestamps. Period times are PostgreSQL `time` values representing local wall-clock time.

| Table | Purpose and important columns |
|---|---|
| `organizations` | `name` and informational IANA `timezone`; contains no student credential |
| `organization_join_codes` | One ungranted 16-character code per organisation, plus rotation timestamp/actor; accessible only through admin RPCs |
| `profiles` | One row per non-anonymous staff user: `org_id`, `display_name`, `role` (`teacher`, `admin`, or reserved `graduate`), `is_active` |
| `timetables` | Named day template with `is_archived` |
| `periods` | Timetable periods with `start_time`, `end_time`, `sort_order`, and `is_lesson`; `end_time > start_time` |
| `classes` | Organisation-scoped class/audience names and sort order |
| `period_classes` | Many-to-many class tags for periods |
| `week_schedule` | One row per organisation/weekday; `0 = Monday … 6 = Sunday`; nullable timetable means no school |
| `date_overrides` | Date-specific timetable or a closed day when `timetable_id` is null |
| `announcements` | Content, expiry, audience, update type, publication time/status, optional HTTPS eMasjid link, and soft-deletion timestamp |
| `student_devices` | Anonymous Auth user to organisation enrolment: `user_id`, `org_id`, `created_at`, `last_seen_at` |
| `audit_log` | Trigger-written before/after history; no client write path |

Announcement audiences are `everyone`, `teachers`, `graduates`, `am`, `pm`, and `specific_class`. A specific-class row must have `audience_class_id`; other audiences must not. Update types are `general`, `class_starts`, `naseehah`, `monthly_programme`, and `yearly_programme`. Status is `draft`, `scheduled`, or `published`.

The `auth.users` trigger creates profiles only for non-anonymous identities. Anonymous identities remain outside `profiles` and gain an organisation only through `enroll_student_device(join_code)`.

Join codes are assigned by `private.assign_join_code()` after organisation insertion. `enroll_student_device` normalises spaces, dashes, and case before lookup. Admin-only RPCs reveal or rotate the code and separately revoke existing devices; the code table itself has no Data API grants or policies.

## Relationships

```text
organizations
├── profiles
├── student_devices
├── timetables ── periods ── period_classes ── classes
├── week_schedule ── timetables
├── date_overrides ── timetables
└── announcements ── profiles (created_by)
                  └── classes (specific audience, optional)

audit_log uses soft references so history survives entity deletion.
```

## RLS and callable functions

RLS is enabled on every public table.

- Active teachers may read their organisation. Administrators additionally receive the existing write policies. Graduates remain reserved and do not gain new capabilities.
- Enrolled anonymous devices may read only their organisation's `organizations`, `timetables`, `periods`, `classes`, `period_classes`, `week_schedule`, `date_overrides`, and eligible `announcements`.
- Student devices cannot read `profiles` or `audit_log` and have no insert, update, or delete policy.
- Student announcement RLS excludes deleted rows, drafts, future publications, and `teachers`/`graduates` audiences. Expiry and class/AM/PM selection remain client display predicates.
- Unenrolled anonymous users resolve no device organisation and read no rows.

`private.current_org_id()` and `private.is_admin()` serve staff policies. `private.current_device_org_id()` serves student-device policies. `public.enroll_student_device(text)` is the first exposed PostgREST RPC; it accepts only an authenticated anonymous JWT and raises `42501` for invalid enrolment.

The release-gating Supabase test matrix covers all staff roles, cross-organisation access, enrolled/unenrolled devices, announcement visibility, signup gating, and device write denial.

## SQLite caches

Both clients mirror the eight synchronized tables: `timetables`, `periods`, `week_schedule`, `date_overrides`, `announcements`, `profiles`, `classes`, and `period_classes`. Every table refresh is a transactional snapshot replacement followed by a `sync_state` update.

Local-only state includes:

```text
sync_state
notification_log
announcement_read
meta
student_preferences     # mobile only
```

The Expo cache uses ordered `PRAGMA user_version` migrations and WAL mode. UUIDs, dates, times, and timestamps are stored as text; booleans are integers. Mobile auth sessions live in chunked SecureStore storage, not SQLite. Signing out or ending a mobile student session wipes the cache and local selection.

The Windows cache stores enrolled desktop student class IDs and AM/PM choices as JSON in the existing `meta` table. Desktop students have an anonymous backend identity and sync only the student-readable table subset; ending the student session clears the persisted selection and encrypted Auth session.
