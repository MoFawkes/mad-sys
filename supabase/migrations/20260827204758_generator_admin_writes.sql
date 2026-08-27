create function public.admin_save_generated_timetable(
    p_timetable_id uuid,
    p_definition jsonb,
    p_blocks jsonb,
    p_anchor_ids uuid[],
    p_periods jsonb
)
returns void language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_date date;
    expanded private.generated_period[];
    supplied_periods private.generated_period[];
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if not exists (
        select 1 from public.timetables timetable
        where timetable.id = p_timetable_id and timetable.org_id = caller_org
    ) then
        raise exception 'The timetable does not belong to your organization' using errcode = '42501';
    end if;
    if jsonb_typeof(p_blocks) <> 'array' or jsonb_typeof(p_periods) <> 'array' then
        raise exception 'Blocks and periods must be arrays' using errcode = '22023';
    end if;
    if exists (
        select 1 from unnest(coalesce(p_anchor_ids, array[]::uuid[])) anchor_id
        left join public.organization_anchors anchor
          on anchor.id = anchor_id and anchor.org_id = caller_org
        where anchor.id is null
    ) then
        raise exception 'An anchor does not belong to your organization' using errcode = '42501';
    end if;
    if exists (
        select 1 from jsonb_to_recordset(p_periods) supplied(id uuid)
        join public.periods period on period.id = supplied.id
        where period.timetable_id <> p_timetable_id
    ) then
        raise exception 'A period id belongs to another timetable' using errcode = '42501';
    end if;

    -- These constraints are checked at COMMIT, outside any caller-side per-item
    -- exception block. Duplicate order values are rejected below; the server
    -- expansion supplies the canonical contiguous period order written later.
    set constraints public.periods_timetable_name_key,
        public.periods_timetable_sort_order_key deferred;

    if exists (
        select 1
        from jsonb_to_recordset(p_blocks) block(sort_order integer)
        group by block.sort_order having count(*) > 1
    ) or exists (
        select 1
        from jsonb_to_recordset(p_periods) period(sort_order integer)
        group by period.sort_order having count(*) > 1
    ) then
        raise exception 'Block and period order values must be unique' using errcode = '23505';
    end if;

    update public.timetables set is_generated = true where id = p_timetable_id;
    insert into public.timetable_generators
        (timetable_id, org_id, session_kind, day_start, advisory_day_end, naming_pattern)
    values (
        p_timetable_id, caller_org, p_definition ->> 'session_kind',
        (p_definition ->> 'day_start')::time,
        nullif(p_definition ->> 'advisory_day_end', '')::time,
        coalesce(nullif(btrim(p_definition ->> 'naming_pattern'), ''), 'Lesson {number}')
    )
    on conflict (timetable_id) do update set
        session_kind = excluded.session_kind, day_start = excluded.day_start,
        advisory_day_end = excluded.advisory_day_end,
        naming_pattern = excluded.naming_pattern;

    delete from public.timetable_generator_blocks where timetable_id = p_timetable_id;
    insert into public.timetable_generator_blocks
        (id, timetable_id, org_id, sort_order, block_kind, name,
         lesson_count, lesson_minutes, break_minutes, hosts_naseehah)
    select block.id, p_timetable_id, caller_org, block.sort_order, block.block_kind,
        block.name, block.lesson_count, block.lesson_minutes, block.break_minutes,
        coalesce(block.hosts_naseehah, false)
    from jsonb_to_recordset(p_blocks) block(
        id uuid, sort_order integer, block_kind text, name text,
        lesson_count integer, lesson_minutes integer, break_minutes integer,
        hosts_naseehah boolean);

    delete from public.timetable_generator_anchors where timetable_id = p_timetable_id;
    insert into public.timetable_generator_anchors (timetable_id, anchor_id, org_id)
    select p_timetable_id, anchor_id, caller_org
    from unnest(coalesce(p_anchor_ids, array[]::uuid[])) anchor_id;

    select timezone(organization.timezone, now())::date into strict target_date
    from public.organizations organization
    where organization.id = caller_org;
    expanded := private.expand_generated_timetable(p_timetable_id, target_date);
    select coalesce(array_agg(row(
        supplied.id, supplied.name, supplied.start_time, supplied.end_time,
        supplied.is_lesson
    )::private.generated_period order by supplied.sort_order), '{}'::private.generated_period[])
    into supplied_periods
    from jsonb_to_recordset(p_periods) supplied(
        id uuid, name text, start_time time, end_time time,
        sort_order integer, is_lesson boolean);
    if supplied_periods is distinct from expanded then
        raise exception 'Client preview does not match the server expansion for %', target_date
            using errcode = '22023';
    end if;

    perform set_config('aqi.generator_write', 'on', true);
    delete from public.periods period
    where period.timetable_id = p_timetable_id
      and not exists (
          select 1 from unnest(expanded) desired where desired.id = period.id
      );
    insert into public.periods
        (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select desired.id, p_timetable_id, desired.name, desired.start_time,
        desired.end_time, desired.ordinality::integer - 1, desired.is_lesson
    from unnest(expanded) with ordinality
        desired(id, name, start_time, end_time, is_lesson, ordinality)
    on conflict (id) do update set
        timetable_id = excluded.timetable_id, name = excluded.name,
        start_time = excluded.start_time, end_time = excluded.end_time,
        sort_order = excluded.sort_order, is_lesson = excluded.is_lesson;
end;
$$;

create function public.admin_bulk_upsert_anchor_date_overrides(
    p_anchor_id uuid,
    p_rows jsonb
)
returns integer language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    affected integer;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if not exists (
        select 1 from public.organization_anchors anchor
        where anchor.id = p_anchor_id and anchor.org_id = caller_org
    ) then
        raise exception 'The anchor does not belong to your organization' using errcode = '42501';
    end if;
    if jsonb_typeof(p_rows) <> 'array' then
        raise exception 'Rows must be an array' using errcode = '22023';
    end if;
    if exists (
        select 1 from jsonb_to_recordset(p_rows) supplied(date date)
        group by supplied.date having count(*) > 1
    ) then
        raise exception 'Each date may appear only once' using errcode = '23505';
    end if;

    insert into public.anchor_date_overrides
        (org_id, anchor_id, date, start_time, duration_minutes, is_cancelled)
    select caller_org, p_anchor_id, supplied.date, supplied.start_time,
        supplied.duration_minutes, coalesce(supplied.is_cancelled, false)
    from jsonb_to_recordset(p_rows) supplied(
        date date, start_time time, duration_minutes integer, is_cancelled boolean)
    on conflict (anchor_id, date) do update set
        start_time = excluded.start_time,
        duration_minutes = excluded.duration_minutes,
        is_cancelled = excluded.is_cancelled;
    get diagnostics affected = row_count;
    return affected;
end;
$$;

revoke all on function public.admin_save_generated_timetable(uuid, jsonb, jsonb, uuid[], jsonb) from public, anon, service_role;
revoke all on function public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb) from public, anon, service_role;
grant execute on function public.admin_save_generated_timetable(uuid, jsonb, jsonb, uuid[], jsonb) to authenticated;
grant execute on function public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb) to authenticated;

-- Preserve the database-wide defence-in-depth invariant for this migration.
revoke all on all tables in schema public from anon;
