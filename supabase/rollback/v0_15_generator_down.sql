-- Emergency logical rollback to the pre-generator schema.
--
-- This removes the entire generator/prayer subsystem, including authored
-- prayer rows and maintenance history. Run only after generated timetables have
-- been disabled/restored and any prayer data that must be retained is exported.
-- The six generator migration-history rows (20260826214012 through
-- 20260925173217) deliberately remain recorded. Never repair or delete hosted
-- migration history; returning to automation afterwards requires a new forward
-- migration.

do $$
begin
    if exists (select 1 from public.timetables where is_generated)
       or exists (select 1 from public.timetable_generators) then
        raise exception 'Disable and restore every generated timetable before rollback';
    end if;
end $$;

drop function if exists public.admin_preview_generated_timetable(uuid, jsonb, date);
drop function if exists public.admin_save_generated_timetable(uuid, jsonb);
drop function if exists public.admin_disable_generated_timetable(uuid);
drop function if exists public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb);
drop function if exists public.admin_save_prayer_fixed_times(jsonb, date);
drop function if exists public.admin_regenerate_generated_timetables();
drop function if exists public.run_generator_maintenance(uuid);
drop function if exists private.generator_maintenance(uuid);
drop function if exists private.generated_periods_json(uuid, private.generated_period[]);
drop function if exists private.expand_generated_timetable(uuid, date);
drop function if exists private.expand_timetable_shape(uuid, uuid, time, integer, integer, integer, integer, boolean, date);
drop function if exists private.apply_generator_anchors(uuid, private.generated_period[], private.resolved_generator_anchor[], uuid, integer, integer);
drop function if exists private.generator_guid_sort_key(uuid);
drop function if exists private.generator_add_minutes(time, integer);
drop function if exists private.generator_stable_id(uuid, text);

drop trigger if exists periods_guard_generated_write on public.periods;
drop trigger if exists organizations_seed_anchors on public.organizations;
drop function if exists private.guard_generated_period_write();
drop function if exists private.seed_organization_anchors();

drop table if exists public.timetable_generator_anchors;
drop table if exists public.timetable_generator_blocks;
drop table if exists public.generator_maintenance_runs;
drop table if exists public.timetable_generators;
drop table if exists public.anchor_date_overrides;
drop table if exists public.anchor_standing_times;
drop table if exists public.organization_anchors;

drop type if exists private.generator_expansion_pass;
drop type if exists private.resolved_generator_anchor;
drop type if exists private.generated_period;

alter table public.timetables drop constraint if exists timetables_id_org_key;
alter table public.timetables drop column if exists is_generated;

-- Restore the released audit helper, whose audited entities all expose `id`.
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
    insert into public.audit_log
        (org_id, actor_id, action, entity_type, entity_id, before, after)
    values (resolved_org_id, resolved_actor_id, lower(tg_op), tg_table_name,
            (source_row ->> 'id')::uuid, row_before, row_after);
    if tg_op = 'DELETE' then return old; end if;
    return new;
end;
$$;

revoke all on function private.audit_row_change() from public, anon, authenticated;

-- Restore the released manual timetable save endpoint.
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
    if exists (select 1 from public.timetables timetable
               where timetable.id = target_timetable_id and timetable.org_id <> caller_org) then
        raise exception 'The timetable belongs to another organization' using errcode = '42501';
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
    values (target_timetable_id, caller_org, p_timetable ->> 'name',
            coalesce((p_timetable ->> 'is_archived')::boolean, false))
    on conflict (id) do update set name = excluded.name, is_archived = excluded.is_archived;
    delete from public.periods period
    where period.timetable_id = target_timetable_id
      and not exists (select 1 from jsonb_to_recordset(p_periods) supplied(id uuid)
                      where supplied.id = period.id);
    insert into public.periods
        (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select supplied.id, target_timetable_id, supplied.name, supplied.start_time,
           supplied.end_time, supplied.sort_order, supplied.is_lesson
    from jsonb_to_recordset(p_periods) supplied(
        id uuid, name text, start_time time, end_time time, sort_order integer, is_lesson boolean)
    on conflict (id) do update set timetable_id = excluded.timetable_id,
        name = excluded.name, start_time = excluded.start_time,
        end_time = excluded.end_time, sort_order = excluded.sort_order,
        is_lesson = excluded.is_lesson;
end;
$$;

revoke all on function public.admin_save_timetable(jsonb, jsonb) from public, anon;
grant execute on function public.admin_save_timetable(jsonb, jsonb) to authenticated;
