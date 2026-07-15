create extension if not exists pgcrypto with schema extensions;

create schema if not exists private;
revoke all on schema private from public, anon;

create table public.organizations (
    id uuid primary key default gen_random_uuid(),
    name text not null,
    timezone text not null default 'Europe/London',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table public.profiles (
    id uuid primary key references auth.users(id) on delete cascade,
    org_id uuid not null references public.organizations(id),
    display_name text not null,
    role text not null default 'staff' constraint profiles_role_check check (role in ('staff', 'admin')),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table public.timetables (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    name text not null,
    is_archived boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint timetables_org_name_key unique (org_id, name)
);

create table public.periods (
    id uuid primary key default gen_random_uuid(),
    timetable_id uuid not null references public.timetables(id) on delete cascade,
    name text not null,
    start_time time not null,
    end_time time not null,
    sort_order integer not null,
    is_lesson boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint periods_valid_time_check check (end_time > start_time),
    constraint periods_timetable_name_key unique (timetable_id, name),
    constraint periods_timetable_sort_order_key unique (timetable_id, sort_order)
);

create table public.week_schedule (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    -- Database convention: 0 = Monday ... 6 = Sunday. .NET DayOfWeek uses Sunday = 0;
    -- Phase 4 adapters must map explicitly rather than casting.
    weekday smallint not null constraint week_schedule_weekday_check check (weekday between 0 and 6),
    timetable_id uuid references public.timetables(id) on delete restrict,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint week_schedule_org_weekday_key unique (org_id, weekday)
);

create table public.date_overrides (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    date date not null,
    timetable_id uuid references public.timetables(id) on delete restrict,
    note text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint date_overrides_org_date_key unique (org_id, date)
);

create table public.announcements (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    title text not null constraint announcements_title_length_check check (char_length(title) <= 200),
    body text not null constraint announcements_body_length_check check (char_length(body) <= 2000),
    expires_at timestamptz,
    created_by uuid not null references public.profiles(id),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table public.audit_log (
    id bigint generated always as identity primary key,
    -- Nullable so system/bootstrap activity can still be captured when no actor org can be resolved.
    org_id uuid references public.organizations(id),
    actor_id uuid,
    action text not null constraint audit_log_action_check check (action in ('insert', 'update', 'delete')),
    entity_type text not null,
    entity_id uuid not null,
    before jsonb,
    after jsonb,
    created_at timestamptz not null default now()
);

create index profiles_org_id_idx on public.profiles (org_id);
create index timetables_org_id_idx on public.timetables (org_id);
create index periods_timetable_id_idx on public.periods (timetable_id);
create index week_schedule_timetable_id_idx on public.week_schedule (timetable_id);
create index date_overrides_org_date_idx on public.date_overrides (org_id, date);
create index date_overrides_timetable_id_idx on public.date_overrides (timetable_id);
create index announcements_org_expires_at_idx on public.announcements (org_id, expires_at);
create index announcements_created_by_idx on public.announcements (created_by);
create index audit_log_org_created_at_idx on public.audit_log (org_id, created_at desc);

create function private.set_updated_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
    new.updated_at := now();
    return new;
end;
$$;

revoke all on function private.set_updated_at() from public, anon, authenticated;

create trigger organizations_set_updated_at before update on public.organizations
for each row execute function private.set_updated_at();
create trigger profiles_set_updated_at before update on public.profiles
for each row execute function private.set_updated_at();
create trigger timetables_set_updated_at before update on public.timetables
for each row execute function private.set_updated_at();
create trigger periods_set_updated_at before update on public.periods
for each row execute function private.set_updated_at();
create trigger week_schedule_set_updated_at before update on public.week_schedule
for each row execute function private.set_updated_at();
create trigger date_overrides_set_updated_at before update on public.date_overrides
for each row execute function private.set_updated_at();
create trigger announcements_set_updated_at before update on public.announcements
for each row execute function private.set_updated_at();
