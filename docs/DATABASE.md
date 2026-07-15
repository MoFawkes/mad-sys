# AQI Clock — Database Design

Status: Draft 1.0 (planning) · Last updated: 2026-07-15

Two databases: **Supabase Postgres** (source of truth) and **SQLite** (per-machine read cache). Server schema lives in `supabase/migrations/*.sql` and is the single source of truth; the SQLite schema mirrors a subset of it.

---

## 1. Supabase Postgres schema

All tables: `id uuid primary key default gen_random_uuid()`, `created_at timestamptz default now()`, `updated_at timestamptz` maintained by a shared `set_updated_at()` trigger. All org data carries `org_id` even though MVP has one organisation (future-proofing without cost — DECISIONS.md ADR-003).

### 1.1 `organizations`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name | text not null | "AQI" |
| timezone | text not null default 'Europe/London' | IANA id; informational for clients (B-1) |

MVP: exactly one row, inserted by seed script.

### 1.2 `profiles`
One row per auth user. Created by an `on_auth_user_created` trigger on `auth.users`.

| Column | Type | Notes |
|---|---|---|
| id | uuid PK, FK → auth.users(id) on delete cascade | |
| org_id | uuid not null FK → organizations | |
| display_name | text not null | Defaults to email local-part; editable by admin |
| role | text not null default 'staff' | CHECK in ('staff','admin') |
| is_active | boolean not null default true | Soft-disable without deleting auth user |

### 1.3 `timetables`
A named single-day template.

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| org_id | uuid not null FK | |
| name | text not null | UNIQUE (org_id, name) |
| is_archived | boolean not null default false | Archived timetables hidden from pickers, kept for history |

### 1.4 `periods`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| timetable_id | uuid not null FK → timetables on delete cascade | |
| name | text not null | UNIQUE (timetable_id, name) |
| start_time | time not null | Local wall-clock (ADR-006) |
| end_time | time not null | CHECK (end_time > start_time) — no midnight crossing |
| sort_order | int not null | UNIQUE (timetable_id, sort_order) |
| is_lesson | boolean not null default true | false = break/assembly/salah (display styling only) |

Overlap between periods is allowed by the DB (warned in UI) — runtime rule in ARCHITECTURE.md §4 resolves it.

### 1.5 `week_schedule`
Default timetable per weekday.

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| org_id | uuid not null FK | |
| weekday | smallint not null | 0=Monday … 6=Sunday; UNIQUE (org_id, weekday) |
| timetable_id | uuid null FK → timetables on delete **restrict** | null = no school |

Seed creates 7 rows (all null) so the app always finds a complete week.

### 1.6 `date_overrides`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| org_id | uuid not null FK | |
| date | date not null | UNIQUE (org_id, date) |
| timetable_id | uuid null FK → timetables on delete **restrict** | null = closed (holiday) |
| note | text | e.g. "Eid holiday", "Mock exams" |

### 1.7 `announcements`
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| org_id | uuid not null FK | |
| title | text not null | ≤ 200 chars (CHECK) |
| body | text not null | ≤ 2000 chars (CHECK) |
| expires_at | timestamptz null | null = until deleted |
| created_by | uuid not null FK → profiles | |

### 1.8 `audit_log`
Written **only** by triggers (SECURITY DEFINER function); no client insert/update/delete.

| Column | Type | Notes |
|---|---|---|
| id | bigint identity PK | |
| org_id | uuid not null | |
| actor_id | uuid null | `auth.uid()` at time of change; null for service-role/seed changes |
| action | text not null | 'insert' / 'update' / 'delete' |
| entity_type | text not null | table name |
| entity_id | uuid not null | |
| before | jsonb null | old row (update/delete) |
| after | jsonb null | new row (insert/update) |
| created_at | timestamptz default now() | |

Audit triggers attach to: `timetables`, `periods`, `week_schedule`, `date_overrides`, `announcements`, `profiles` (role changes).

### 1.9 Relationships (summary)

```
organizations 1--* profiles
organizations 1--* timetables 1--* periods
organizations 1--* week_schedule *--1 timetables (nullable, RESTRICT)
organizations 1--* date_overrides *--1 timetables (nullable, RESTRICT)
organizations 1--* announcements *--1 profiles (created_by)
audit_log — soft references only (survives entity deletion)
```

Indexes beyond PK/unique: `periods(timetable_id)`, `date_overrides(org_id, date)`, `announcements(org_id, expires_at)`, `audit_log(org_id, created_at desc)`.

---

## 2. Row Level Security approach

Full policy detail in SECURITY.md §3; design summary:

- RLS **enabled on every table**; no table readable without an authenticated JWT.
- Helper functions `current_org_id()` and `is_admin()` (SECURITY DEFINER, reading `profiles` by `auth.uid()`) keep policies one-liners and consistent.
- Read policies: any active member of the org can `SELECT` all org rows (staff need timetables, announcements, profile display names for audit/announcement attribution).
- Write policies: `INSERT/UPDATE/DELETE` require `is_admin()` and row `org_id = current_org_id()`.
- `profiles`: users can update **only** their own `display_name`; `role`/`is_active`/`org_id` changes require admin (enforced by a separate column-guard trigger, since RLS is row-level not column-level).
- `audit_log`: `SELECT` for admins only; no write policies at all (trigger writes bypass RLS via SECURITY DEFINER).
- Realtime respects RLS automatically (Supabase Realtime authorization), so staff receive change events only for rows they can read — which is everything in their org, so no special handling.

---

## 3. SQLite cache schema

File: `%LOCALAPPDATA%\AqiClock\cache.db` (WAL mode). Purpose: read cache + local notification dedup. **Never** the source of truth; safe to delete (app re-syncs).

Mirror tables — same columns as server minus what the client doesn't need:

```sql
organizations (id TEXT PK, name TEXT, timezone TEXT)
profiles      (id TEXT PK, display_name TEXT, role TEXT, is_active INTEGER)
timetables    (id TEXT PK, name TEXT, is_archived INTEGER)
periods       (id TEXT PK, timetable_id TEXT, name TEXT,
               start_time TEXT, end_time TEXT, sort_order INTEGER, is_lesson INTEGER)
week_schedule (weekday INTEGER PK, timetable_id TEXT NULL)
date_overrides(id TEXT PK, date TEXT, timetable_id TEXT NULL, note TEXT)
announcements (id TEXT PK, title TEXT, body TEXT, expires_at TEXT NULL,
               created_by TEXT, created_at TEXT)
```

Conventions: uuids as TEXT; times/timestamps as ISO-8601 TEXT; booleans as INTEGER. `org_id` is dropped — the cache holds exactly one org's data; if the signed-in user's org ever differs from the cached org (`meta.org_id`), the cache is wiped and re-pulled.

Local-only tables:

```sql
meta             (key TEXT PK, value TEXT)          -- schema_version, org_id, current_user_id
sync_state       (table_name TEXT PK, last_synced_at TEXT)
notification_log (event_key TEXT PK, fired_at TEXT NULL, skipped INTEGER NOT NULL DEFAULT 0)
announcement_read(announcement_id TEXT PK, read_at TEXT)
```

Snapshot replace: each table refresh = `BEGIN; DELETE FROM x; INSERT …; UPDATE sync_state; COMMIT;` so readers never see a partially synced table.

Migrations: `meta.schema_version` + ordered embedded SQL scripts. On migration failure or corruption (`PRAGMA integrity_check`), delete and recreate the cache — it is disposable by design.

Not in SQLite: user settings (JSON file), auth session (DPAPI-encrypted file), logs (files). Audit log is not cached — the admin audit screen is online-only (trivial data, avoids syncing an append-only table).
