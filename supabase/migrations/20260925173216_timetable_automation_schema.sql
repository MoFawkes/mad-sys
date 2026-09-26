-- v0.15 timetable automation is a forward-only simplification of the generator
-- schema already applied through 20260827223000. Shared tables and their data
-- stay in place. Only the obsolete block and timetable-anchor authoring tables
-- are removed.

-- Fail closed if anybody authors generator data between rehearsal and deploy.
-- The old authoring model cannot be converted losslessly to the six-field shape.
do $$
declare
    generated_timetables integer;
    generator_rows integer;
    block_rows integer;
    timetable_anchor_rows integer;
begin
    select count(*) into generated_timetables
    from public.timetables where is_generated;
    select count(*) into generator_rows
    from public.timetable_generators;
    select count(*) into block_rows
    from public.timetable_generator_blocks;
    select count(*) into timetable_anchor_rows
    from public.timetable_generator_anchors;

    if generated_timetables <> 0 or generator_rows <> 0
       or block_rows <> 0 or timetable_anchor_rows <> 0 then
        raise exception
            'Cannot simplify timetable automation while old generator data exists'
            using errcode = '55000',
                  detail = format(
                      'generated timetables=%s, generator rows=%s, block rows=%s, timetable-anchor rows=%s',
                      generated_timetables, generator_rows, block_rows, timetable_anchor_rows),
                  hint = 'Disable or remove every old generated timetable before applying v0.15.';
    end if;
end;
$$;

-- Remove every old entry point and helper by exact signature. Different
-- argument lists would otherwise survive as callable overloads, while matching
-- signatures would make the replacement CREATE statements fail.
drop function if exists public.admin_preview_generated_timetable(uuid, jsonb, jsonb, uuid[]);
drop function if exists public.admin_save_generated_timetable(uuid, jsonb, jsonb, uuid[], jsonb);
drop function if exists public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb);
drop function if exists public.admin_regenerate_generated_timetables();
drop function if exists public.run_generator_maintenance(uuid);
drop function if exists private.generator_maintenance(uuid);
drop function if exists private.expand_generated_timetable(uuid, date);
drop function if exists private.apply_generator_anchors(
    uuid, private.generated_period[], private.resolved_generator_anchor[],
    uuid, integer, integer);
drop function if exists private.generator_guid_sort_key(uuid);
drop function if exists private.generator_add_minutes(time, integer);
drop function if exists private.generator_stable_id(uuid, text);

-- These are the only tables discarded by the simplified authoring model.
-- Their update/audit triggers, policies, grants, indexes and foreign keys are
-- removed with the tables.
drop table public.timetable_generator_anchors;
drop table public.timetable_generator_blocks;

-- Preserve all prayer data while tightening the same validation bounds used by
-- the v0.15 form.
alter table public.anchor_standing_times
drop constraint anchor_standing_times_duration_check,
add constraint anchor_standing_times_duration_check
    check (duration_minutes between 1 and 120);

alter table public.anchor_date_overrides
drop constraint anchor_date_overrides_duration_check,
add constraint anchor_date_overrides_duration_check
    check (duration_minutes between 1 and 120);

alter index public.anchor_standing_times_org_anchor_idx
rename to anchor_standing_times_org_anchor_effective_idx;

-- The guard above proves this table is empty, so the old shape columns can be
-- replaced without inventing a conversion for data the school never authored.
-- The existing PK, org FK, composite timetable/org FK, timestamps, RLS state,
-- audit trigger and updated_at trigger all remain in place.
alter table public.timetable_generators
drop constraint timetable_generators_session_kind_check,
drop constraint timetable_generators_naming_pattern_check,
drop constraint timetable_generators_id_org_key,
drop constraint timetable_generators_timetable_id_fkey,
drop column session_kind,
drop column advisory_day_end,
drop column naming_pattern,
add column lesson_count integer not null,
add column lesson_minutes integer not null,
add column break_after_lesson integer,
add column break_minutes integer,
add column adjusts_for_prayer boolean not null default true,
add column pre_conversion_periods jsonb not null,
add constraint timetable_generators_lesson_count_check
    check (lesson_count between 1 and 20),
add constraint timetable_generators_lesson_minutes_check
    check (lesson_minutes between 5 and 120),
add constraint timetable_generators_break_minutes_check
    check (break_minutes between 5 and 120),
add constraint timetable_generators_restore_array_check
    check (jsonb_typeof(pre_conversion_periods) = 'array'),
add constraint timetable_generators_break_pair_check check (
    (break_after_lesson is null and break_minutes is null)
    or (break_after_lesson is not null and break_minutes is not null
        and break_after_lesson between 1 and lesson_count - 1)
);

comment on column public.timetable_generators.pre_conversion_periods is
    'Exact manual period snapshot captured once at opt-in; generated periods are read-only until restore.';

-- Direct writes from authenticated clients belonged to the discarded general
-- authoring model. v0.15 writes only through validated SECURITY DEFINER RPCs.
drop policy if exists organization_anchors_insert_admin on public.organization_anchors;
drop policy if exists organization_anchors_update_admin on public.organization_anchors;
drop policy if exists organization_anchors_delete_admin on public.organization_anchors;
drop policy if exists anchor_standing_times_insert_admin on public.anchor_standing_times;
drop policy if exists anchor_standing_times_update_admin on public.anchor_standing_times;
drop policy if exists anchor_standing_times_delete_admin on public.anchor_standing_times;
drop policy if exists anchor_date_overrides_insert_admin on public.anchor_date_overrides;
drop policy if exists anchor_date_overrides_update_admin on public.anchor_date_overrides;
drop policy if exists anchor_date_overrides_delete_admin on public.anchor_date_overrides;
drop policy if exists timetable_generators_insert_admin on public.timetable_generators;
drop policy if exists timetable_generators_update_admin on public.timetable_generators;
drop policy if exists timetable_generators_delete_admin on public.timetable_generators;

revoke all on public.organization_anchors, public.anchor_standing_times,
    public.anchor_date_overrides, public.timetable_generators,
    public.generator_maintenance_runs
from public, anon, authenticated;
grant select on public.organization_anchors, public.anchor_standing_times,
    public.anchor_date_overrides, public.timetable_generators
to authenticated;
grant select on public.generator_maintenance_runs to authenticated;
revoke all on all tables in schema public from anon;

-- Keep the seed trigger, but replace its function and trigger deliberately so
-- the migration is explicit about the surviving behaviour.
create or replace function private.seed_organization_anchors()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
    insert into public.organization_anchors (org_id, key, name, sort_order)
    values (new.id, 'zuhr', 'Zuhr', 0), (new.id, 'asr', 'Asr', 1),
           (new.id, 'maghrib', 'Maghrib', 2), (new.id, 'isha', 'Isha', 3)
    on conflict (org_id, key) do nothing;
    return new;
end;
$$;

revoke all on function private.seed_organization_anchors() from public, anon, authenticated;
drop trigger if exists organizations_seed_anchors on public.organizations;
create trigger organizations_seed_anchors after insert on public.organizations
for each row execute function private.seed_organization_anchors();

-- Preserve existing anchor ids and authored rows; this only fills any missing
-- anchor natural keys for organisations created before the seed trigger.
insert into public.organization_anchors (org_id, key, name, sort_order)
select organization.id, anchor.key, anchor.name, anchor.sort_order
from public.organizations organization
cross join (values ('zuhr', 'Zuhr', 0), ('asr', 'Asr', 1),
                   ('maghrib', 'Maghrib', 2), ('isha', 'Isha', 3))
    anchor(key, name, sort_order)
on conflict (org_id, key) do nothing;

-- Natural-key tables need a stable fallback entity id in the common audit
-- trigger. This helper is table-agnostic and has no dependency on the two
-- discarded authoring tables.
create or replace function private.audit_row_change()
returns trigger language plpgsql security definer set search_path = '' as $$
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
        select timetable.org_id into resolved_org_id
        from public.timetables timetable
        where timetable.id = (source_row ->> 'timetable_id')::uuid;
    end if;
    if resolved_org_id is null and resolved_actor_id is not null then
        select profile.org_id into resolved_org_id
        from public.profiles profile where profile.id = resolved_actor_id;
    end if;
    resolved_entity_id := coalesce(
        nullif(source_row ->> 'id', '')::uuid,
        nullif(source_row ->> 'timetable_id', '')::uuid,
        nullif(source_row ->> 'anchor_id', '')::uuid);

    insert into public.audit_log
        (org_id, actor_id, action, entity_type, entity_id, before, after)
    values (resolved_org_id, resolved_actor_id, lower(tg_op), tg_table_name,
            resolved_entity_id, row_before, row_after);
    if tg_op = 'DELETE' then return old; end if;
    return new;
end;
$$;

revoke all on function private.audit_row_change() from public, anon, authenticated;

create or replace function private.guard_generated_period_write()
returns trigger language plpgsql security invoker set search_path = '' as $$
declare
    owner_timetable_id uuid :=
        case when tg_op = 'DELETE' then old.timetable_id else new.timetable_id end;
begin
    if coalesce(current_setting('aqi.generator_write', true), '') <> 'on'
       and exists (select 1 from public.timetables timetable
                   where timetable.id = owner_timetable_id
                     and timetable.is_generated) then
        raise exception 'Generated timetable periods are read-only'
            using errcode = '55000';
    end if;
    if tg_op = 'DELETE' then return old; end if;
    return new;
end;
$$;

revoke all on function private.guard_generated_period_write() from public, anon, authenticated;
drop trigger if exists periods_guard_generated_write on public.periods;
create trigger periods_guard_generated_write
before insert or update or delete on public.periods
for each row execute function private.guard_generated_period_write();

-- Keep the legacy manual save endpoint from changing generated rows or their
-- timetable metadata. The generated save endpoint is authoritative instead.
create or replace function public.admin_save_timetable(
    p_timetable jsonb,
    p_periods jsonb
)
returns void language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_timetable_id uuid := (p_timetable ->> 'id')::uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if exists (select 1 from public.timetables timetable
               where timetable.id = target_timetable_id
                 and timetable.org_id <> caller_org) then
        raise exception 'The timetable belongs to another organization'
            using errcode = '42501';
    end if;
    if exists (select 1 from public.timetables timetable
               where timetable.id = target_timetable_id
                 and timetable.is_generated) then
        raise exception 'Generated timetables cannot be edited as period rows'
            using errcode = '55000';
    end if;
    if exists (
        select 1 from jsonb_to_recordset(p_periods) supplied(id uuid)
        join public.periods period on period.id = supplied.id
        join public.timetables owner on owner.id = period.timetable_id
        where owner.org_id <> caller_org
    ) then
        raise exception 'A period belongs to another organization'
            using errcode = '42501';
    end if;
    set constraints public.periods_timetable_name_key,
        public.periods_timetable_sort_order_key deferred;
    insert into public.timetables (id, org_id, name, is_archived)
    values (target_timetable_id, caller_org, p_timetable ->> 'name',
            coalesce((p_timetable ->> 'is_archived')::boolean, false))
    on conflict (id) do update
    set name = excluded.name, is_archived = excluded.is_archived;
    delete from public.periods period
    where period.timetable_id = target_timetable_id
      and not exists (
          select 1 from jsonb_to_recordset(p_periods) supplied(id uuid)
          where supplied.id = period.id);
    insert into public.periods
        (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select supplied.id, target_timetable_id, supplied.name, supplied.start_time,
           supplied.end_time, supplied.sort_order, supplied.is_lesson
    from jsonb_to_recordset(p_periods) supplied(
        id uuid, name text, start_time time, end_time time,
        sort_order integer, is_lesson boolean)
    on conflict (id) do update
    set timetable_id = excluded.timetable_id,
        name = excluded.name,
        start_time = excluded.start_time,
        end_time = excluded.end_time,
        sort_order = excluded.sort_order,
        is_lesson = excluded.is_lesson;
end;
$$;

revoke all on function public.admin_save_timetable(jsonb, jsonb)
from public, anon;
grant execute on function public.admin_save_timetable(jsonb, jsonb)
to authenticated;
