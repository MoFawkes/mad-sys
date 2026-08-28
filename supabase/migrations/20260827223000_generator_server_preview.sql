create function public.admin_preview_generated_timetable(
    p_timetable_id uuid,
    p_definition jsonb,
    p_blocks jsonb,
    p_anchor_ids uuid[]
)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_date date;
    expanded private.generated_period[];
    rollback_marker constant text := 'aqi.generator_preview.rollback';
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if not exists (
        select 1 from public.timetables
        where id = p_timetable_id and org_id = caller_org
    ) then
        raise exception 'The timetable does not belong to your organization' using errcode = '42501';
    end if;
    if jsonb_typeof(p_blocks) <> 'array' then
        raise exception 'Blocks must be an array' using errcode = '22023';
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
        select 1 from jsonb_to_recordset(p_blocks) block(sort_order integer)
        group by block.sort_order having count(*) > 1
    ) then
        raise exception 'Block order values must be unique' using errcode = '23505';
    end if;
    select timezone(organization.timezone, now())::date into strict target_date
    from public.organizations organization where organization.id = caller_org;

    -- Expansion reads normalized generator tables. Stage the proposed definition
    -- in a subtransaction, retain its result, and deliberately roll every write
    -- (including audit triggers) back before returning the preview.
    begin
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

        expanded := private.expand_generated_timetable(p_timetable_id, target_date);
        raise exception '%', rollback_marker;
    exception when raise_exception then
        if sqlerrm <> rollback_marker then raise; end if;
    end;

    return jsonb_build_object(
        'date', target_date,
        'periods', coalesce((
            select jsonb_agg(jsonb_build_object(
                'id', period.id, 'timetable_id', p_timetable_id,
                'name', period.name, 'start_time', period.start_time,
                'end_time', period.end_time,
                'sort_order', period.ordinality::integer - 1,
                'is_lesson', period.is_lesson
            ) order by period.ordinality)
            from unnest(expanded) with ordinality
                period(id, name, start_time, end_time, is_lesson, ordinality)
        ), '[]'::jsonb)
    );
end;
$$;

revoke all on function public.admin_preview_generated_timetable(uuid, jsonb, jsonb, uuid[])
from public, anon, service_role;
grant execute on function public.admin_preview_generated_timetable(uuid, jsonb, jsonb, uuid[])
to authenticated;

revoke all on all tables in schema public from anon;
