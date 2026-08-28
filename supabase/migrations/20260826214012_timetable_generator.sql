alter table public.timetables
add column is_generated boolean not null default false;

alter table public.timetables
add constraint timetables_id_org_key unique (id, org_id);

create table public.organization_anchors (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    key text not null constraint organization_anchors_key_check
        check (key in ('zuhr', 'asr', 'maghrib', 'isha')),
    name text not null constraint organization_anchors_name_check check (btrim(name) <> ''),
    sort_order integer not null constraint organization_anchors_sort_order_check check (sort_order >= 0),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint organization_anchors_org_key_key unique (org_id, key),
    constraint organization_anchors_org_sort_order_key unique (org_id, sort_order),
    constraint organization_anchors_id_org_key unique (id, org_id)
);

create table public.anchor_standing_times (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    anchor_id uuid not null,
    weekday smallint constraint anchor_standing_times_weekday_check
        check (weekday between 0 and 6),
    start_time time not null,
    -- Nullable deliberately: Friday Zuhr must be authored without inventing a duration.
    duration_minutes integer constraint anchor_standing_times_duration_check
        check (duration_minutes > 0),
    effective_from date not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint anchor_standing_times_anchor_org_fkey
        foreign key (anchor_id, org_id)
        references public.organization_anchors(id, org_id) on delete cascade
);

-- NULL weekdays need a separate index because NULL values are distinct in a normal unique key.
create unique index anchor_standing_times_default_key
on public.anchor_standing_times (anchor_id, effective_from)
where weekday is null;
create unique index anchor_standing_times_weekday_key
on public.anchor_standing_times (anchor_id, weekday, effective_from)
where weekday is not null;

create table public.anchor_date_overrides (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    anchor_id uuid not null,
    date date not null,
    start_time time,
    duration_minutes integer constraint anchor_date_overrides_duration_check
        check (duration_minutes > 0),
    is_cancelled boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint anchor_date_overrides_anchor_org_fkey
        foreign key (anchor_id, org_id)
        references public.organization_anchors(id, org_id) on delete cascade,
    constraint anchor_date_overrides_anchor_date_key unique (anchor_id, date),
    constraint anchor_date_overrides_value_check check (
        (is_cancelled and start_time is null and duration_minutes is null)
        or (not is_cancelled and start_time is not null)
    )
);

create table public.timetable_generators (
    timetable_id uuid primary key references public.timetables(id) on delete cascade,
    org_id uuid not null references public.organizations(id),
    session_kind text not null constraint timetable_generators_session_kind_check
        check (session_kind in ('am', 'pm')),
    day_start time not null,
    advisory_day_end time,
    naming_pattern text not null default 'Lesson {number}'
        constraint timetable_generators_naming_pattern_check check (btrim(naming_pattern) <> ''),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint timetable_generators_timetable_org_fkey
        foreign key (timetable_id, org_id)
        references public.timetables(id, org_id) on delete cascade,
    constraint timetable_generators_id_org_key unique (timetable_id, org_id)
);

create table public.timetable_generator_blocks (
    id uuid primary key default gen_random_uuid(),
    timetable_id uuid not null,
    org_id uuid not null references public.organizations(id),
    sort_order integer not null constraint timetable_generator_blocks_sort_order_check
        check (sort_order >= 0),
    block_kind text not null constraint timetable_generator_blocks_kind_check
        check (block_kind in ('lessons', 'break')),
    name text,
    lesson_count integer,
    lesson_minutes integer,
    break_minutes integer,
    hosts_naseehah boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint timetable_generator_blocks_generator_org_fkey
        foreign key (timetable_id, org_id)
        references public.timetable_generators(timetable_id, org_id) on delete cascade,
    constraint timetable_generator_blocks_timetable_sort_order_key
        unique (timetable_id, sort_order),
    constraint timetable_generator_blocks_shape_check check (
        (block_kind = 'lessons'
            and lesson_count is not null and lesson_count > 0
            and lesson_minutes is not null and lesson_minutes > 0
            and break_minutes is null and not hosts_naseehah)
        or
        (block_kind = 'break'
            and name is not null and btrim(name) <> ''
            and break_minutes is not null and break_minutes > 0
            and lesson_count is null and lesson_minutes is null)
    )
);

create table public.timetable_generator_anchors (
    timetable_id uuid not null,
    anchor_id uuid not null,
    org_id uuid not null references public.organizations(id),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    primary key (timetable_id, anchor_id),
    constraint timetable_generator_anchors_generator_org_fkey
        foreign key (timetable_id, org_id)
        references public.timetable_generators(timetable_id, org_id) on delete cascade,
    constraint timetable_generator_anchors_anchor_org_fkey
        foreign key (anchor_id, org_id)
        references public.organization_anchors(id, org_id) on delete cascade
);

create index anchor_standing_times_org_anchor_idx
on public.anchor_standing_times (org_id, anchor_id, effective_from desc);
create index anchor_date_overrides_org_date_idx
on public.anchor_date_overrides (org_id, date);
create index timetable_generator_blocks_org_timetable_idx
on public.timetable_generator_blocks (org_id, timetable_id);
create index timetable_generator_anchors_org_timetable_idx
on public.timetable_generator_anchors (org_id, timetable_id);

create function private.seed_organization_anchors()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
    insert into public.organization_anchors (org_id, key, name, sort_order)
    values
        (new.id, 'zuhr', 'Zuhr', 0),
        (new.id, 'asr', 'Asr', 1),
        (new.id, 'maghrib', 'Maghrib', 2),
        (new.id, 'isha', 'Isha', 3)
    on conflict (org_id, key) do nothing;
    return new;
end;
$$;
revoke all on function private.seed_organization_anchors() from public, anon, authenticated;
create trigger organizations_seed_anchors after insert on public.organizations
for each row execute function private.seed_organization_anchors();

insert into public.organization_anchors (org_id, key, name, sort_order)
select organization.id, anchor.key, anchor.name, anchor.sort_order
from public.organizations as organization
cross join (values
    ('zuhr', 'Zuhr', 0),
    ('asr', 'Asr', 1),
    ('maghrib', 'Maghrib', 2),
    ('isha', 'Isha', 3)
) as anchor(key, name, sort_order)
on conflict (org_id, key) do nothing;

-- The original audit helper assumed every audited row exposed an `id`. Generator
-- definition tables intentionally use natural composite keys, so use the owning
-- timetable id as their stable audit entity id.
create or replace function private.audit_row_change()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    row_before jsonb;
    row_after jsonb;
    source_row jsonb;
    resolved_org_id uuid;
    resolved_actor_id uuid := auth.uid();
    resolved_entity_id uuid;
begin
    row_before := case when tg_op in ('UPDATE', 'DELETE') then to_jsonb(old) end;
    row_after := case when tg_op in ('INSERT', 'UPDATE') then to_jsonb(new) end;
    source_row := coalesce(row_after, row_before);

    if source_row ? 'org_id' then
        resolved_org_id := nullif(source_row ->> 'org_id', '')::uuid;
    elsif tg_table_name = 'periods' then
        select t.org_id into resolved_org_id
        from public.timetables as t
        where t.id = (source_row ->> 'timetable_id')::uuid;
    end if;

    if resolved_org_id is null and resolved_actor_id is not null then
        select p.org_id into resolved_org_id
        from public.profiles as p
        where p.id = resolved_actor_id;
    end if;

    resolved_entity_id := coalesce(
        nullif(source_row ->> 'id', '')::uuid,
        nullif(source_row ->> 'timetable_id', '')::uuid,
        nullif(source_row ->> 'anchor_id', '')::uuid
    );

    insert into public.audit_log (
        org_id, actor_id, action, entity_type, entity_id, before, after
    ) values (
        resolved_org_id, resolved_actor_id, lower(tg_op), tg_table_name,
        resolved_entity_id, row_before, row_after
    );

    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;

revoke all on function private.audit_row_change() from public, anon, authenticated;

create function private.guard_generated_period_write()
returns trigger language plpgsql security invoker set search_path = '' as $$
declare
    owner_timetable_id uuid := case when tg_op = 'DELETE' then old.timetable_id else new.timetable_id end;
begin
    if coalesce(current_setting('aqi.generator_write', true), '') <> 'on'
       and exists (
           select 1 from public.timetables as timetable
           where timetable.id = owner_timetable_id and timetable.is_generated
       ) then
        raise exception 'Generated timetable periods are read-only'
            using errcode = '55000';
    end if;
    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;
revoke all on function private.guard_generated_period_write() from public, anon, authenticated;
create trigger periods_guard_generated_write
before insert or update or delete on public.periods
for each row execute function private.guard_generated_period_write();

create trigger organization_anchors_set_updated_at before update on public.organization_anchors
for each row execute function private.set_updated_at();
create trigger anchor_standing_times_set_updated_at before update on public.anchor_standing_times
for each row execute function private.set_updated_at();
create trigger anchor_date_overrides_set_updated_at before update on public.anchor_date_overrides
for each row execute function private.set_updated_at();
create trigger timetable_generators_set_updated_at before update on public.timetable_generators
for each row execute function private.set_updated_at();
create trigger timetable_generator_blocks_set_updated_at before update on public.timetable_generator_blocks
for each row execute function private.set_updated_at();
create trigger timetable_generator_anchors_set_updated_at before update on public.timetable_generator_anchors
for each row execute function private.set_updated_at();

create trigger organization_anchors_audit after insert or update or delete on public.organization_anchors
for each row execute function private.audit_row_change();
create trigger anchor_standing_times_audit after insert or update or delete on public.anchor_standing_times
for each row execute function private.audit_row_change();
create trigger anchor_date_overrides_audit after insert or update or delete on public.anchor_date_overrides
for each row execute function private.audit_row_change();
create trigger timetable_generators_audit after insert or update or delete on public.timetable_generators
for each row execute function private.audit_row_change();
create trigger timetable_generator_blocks_audit after insert or update or delete on public.timetable_generator_blocks
for each row execute function private.audit_row_change();
create trigger timetable_generator_anchors_audit after insert or update or delete on public.timetable_generator_anchors
for each row execute function private.audit_row_change();

revoke all on public.organization_anchors from anon;
revoke all on public.anchor_standing_times from anon;
revoke all on public.anchor_date_overrides from anon;
revoke all on public.timetable_generators from anon;
revoke all on public.timetable_generator_blocks from anon;
revoke all on public.timetable_generator_anchors from anon;
-- Earlier migrations revoked anon only for the tables that existed at that
-- point. Reassert the database-wide invariant so later-created public tables
-- cannot retain legacy automatic Data API grants.
revoke all on all tables in schema public from anon;

grant select, insert, update, delete on public.organization_anchors to authenticated;
grant select, insert, update, delete on public.anchor_standing_times to authenticated;
grant select, insert, update, delete on public.anchor_date_overrides to authenticated;
grant select, insert, update, delete on public.timetable_generators to authenticated;
grant select, insert, update, delete on public.timetable_generator_blocks to authenticated;
grant select, insert, update, delete on public.timetable_generator_anchors to authenticated;

alter table public.organization_anchors enable row level security;
alter table public.anchor_standing_times enable row level security;
alter table public.anchor_date_overrides enable row level security;
alter table public.timetable_generators enable row level security;
alter table public.timetable_generator_blocks enable row level security;
alter table public.timetable_generator_anchors enable row level security;

create policy organization_anchors_select_staff on public.organization_anchors
for select to authenticated using (org_id = (select private.current_org_id()));
create policy organization_anchors_insert_admin on public.organization_anchors
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy organization_anchors_update_admin on public.organization_anchors
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy organization_anchors_delete_admin on public.organization_anchors
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy anchor_standing_times_select_staff on public.anchor_standing_times
for select to authenticated using (org_id = (select private.current_org_id()));
create policy anchor_standing_times_insert_admin on public.anchor_standing_times
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy anchor_standing_times_update_admin on public.anchor_standing_times
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy anchor_standing_times_delete_admin on public.anchor_standing_times
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy anchor_date_overrides_select_staff on public.anchor_date_overrides
for select to authenticated using (org_id = (select private.current_org_id()));
create policy anchor_date_overrides_insert_admin on public.anchor_date_overrides
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy anchor_date_overrides_update_admin on public.anchor_date_overrides
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy anchor_date_overrides_delete_admin on public.anchor_date_overrides
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy timetable_generators_select_staff on public.timetable_generators
for select to authenticated using (org_id = (select private.current_org_id()));
create policy timetable_generators_insert_admin on public.timetable_generators
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generators_update_admin on public.timetable_generators
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generators_delete_admin on public.timetable_generators
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy timetable_generator_blocks_select_staff on public.timetable_generator_blocks
for select to authenticated using (org_id = (select private.current_org_id()));
create policy timetable_generator_blocks_insert_admin on public.timetable_generator_blocks
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generator_blocks_update_admin on public.timetable_generator_blocks
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generator_blocks_delete_admin on public.timetable_generator_blocks
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy timetable_generator_anchors_select_staff on public.timetable_generator_anchors
for select to authenticated using (org_id = (select private.current_org_id()));
create policy timetable_generator_anchors_insert_admin on public.timetable_generator_anchors
for insert to authenticated with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generator_anchors_update_admin on public.timetable_generator_anchors
for update to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetable_generator_anchors_delete_admin on public.timetable_generator_anchors
for delete to authenticated using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create or replace function public.admin_save_timetable(p_timetable jsonb, p_periods jsonb)
returns void language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_timetable_id uuid := (p_timetable ->> 'id')::uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if exists (
        select 1 from public.timetables as t
        where t.id = target_timetable_id and t.org_id <> caller_org
    ) then
        raise exception 'The timetable belongs to another organization' using errcode = '42501';
    end if;
    if exists (
        select 1 from public.timetables as t
        where t.id = target_timetable_id and t.is_generated
    ) then
        raise exception 'Generated timetables cannot be edited as period rows' using errcode = '55000';
    end if;
    if exists (
        select 1 from jsonb_to_recordset(p_periods) supplied(id uuid)
        join public.periods period on period.id = supplied.id
        join public.timetables owner on owner.id = period.timetable_id
        where owner.org_id <> caller_org
    ) then
        raise exception 'A period belongs to another organization' using errcode = '42501';
    end if;
    set constraints public.periods_timetable_name_key, public.periods_timetable_sort_order_key deferred;
    insert into public.timetables (id, org_id, name, is_archived)
    values (target_timetable_id, caller_org, p_timetable ->> 'name', coalesce((p_timetable ->> 'is_archived')::boolean, false))
    on conflict (id) do update set name = excluded.name, is_archived = excluded.is_archived;
    delete from public.periods period
    where period.timetable_id = target_timetable_id
      and not exists (select 1 from jsonb_to_recordset(p_periods) supplied(id uuid) where supplied.id = period.id);
    insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select supplied.id, target_timetable_id, supplied.name, supplied.start_time, supplied.end_time, supplied.sort_order, supplied.is_lesson
    from jsonb_to_recordset(p_periods) supplied(id uuid, name text, start_time time, end_time time, sort_order integer, is_lesson boolean)
    on conflict (id) do update set timetable_id = excluded.timetable_id, name = excluded.name,
        start_time = excluded.start_time, end_time = excluded.end_time,
        sort_order = excluded.sort_order, is_lesson = excluded.is_lesson;
end;
$$;

revoke all on function public.admin_save_timetable(jsonb, jsonb) from public, anon;
grant execute on function public.admin_save_timetable(jsonb, jsonb) to authenticated;
