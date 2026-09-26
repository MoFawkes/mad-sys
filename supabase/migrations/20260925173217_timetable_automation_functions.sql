-- The private composite types were installed by 20260826225951 and are
-- intentionally unchanged. 20260925173216 removed every old function that
-- depended on them, so the simplified functions below can reuse the types
-- without creating duplicate objects or leaving callable overloads behind.

create function private.generator_stable_id(p_timetable_id uuid, p_identity text)
returns uuid language plpgsql immutable strict security invoker set search_path = '' as $$
declare
    hash text := encode(extensions.digest(
        replace(lower(p_timetable_id::text), '-', '') || ':' || p_identity, 'sha256'), 'hex');
begin
    -- Match new Guid(SHA256[..16]) in .NET (the first three fields are formatted
    -- little-endian). SqlExpansionMatchesDomainFixtures protects this contract.
    return (
        substr(hash, 7, 2) || substr(hash, 5, 2) || substr(hash, 3, 2) || substr(hash, 1, 2) || '-' ||
        substr(hash, 11, 2) || substr(hash, 9, 2) || '-' ||
        substr(hash, 15, 2) || substr(hash, 13, 2) || '-' ||
        substr(hash, 17, 4) || '-' || substr(hash, 21, 12)
    )::uuid;
end;
$$;

create function private.generator_add_minutes(p_time time, p_minutes integer)
returns time language plpgsql immutable strict security invoker set search_path = '' as $$
declare
    minute_of_day integer := (extract(epoch from p_time)::integer / 60) + p_minutes;
begin
    if minute_of_day < 0 or minute_of_day > 1439 then
        raise exception 'Generated periods cannot cross midnight' using errcode = '22008';
    end if;
    return time '00:00' + make_interval(mins => minute_of_day);
end;
$$;

create function private.generator_guid_sort_key(p_id uuid)
returns bytea language sql immutable strict security invoker set search_path = ''
as $$ select decode(replace(lower(p_id::text), '-', ''), 'hex'); $$;

create function private.apply_generator_anchors(
    p_timetable_id uuid,
    p_authored private.generated_period[],
    p_anchors private.resolved_generator_anchor[],
    p_naseehah_anchor_id uuid,
    p_prayer_minutes integer,
    p_naseehah_minutes integer
)
returns private.generator_expansion_pass
language plpgsql security invoker set search_path = '' as $$
declare
    generated private.generated_period[] := coalesce(p_authored, '{}'::private.generated_period[]);
    applied uuid[] := '{}'::uuid[];
    anchor private.resolved_generator_anchor;
    source private.generated_period;
    containing integer;
    insertion integer;
    duration integer;
    total integer;
    anchor_name text;
    before_periods private.generated_period[];
    after_periods private.generated_period[];
begin
    if coalesce(array_length(p_anchors, 1), 0) = 0 then
        return row(generated, applied)::private.generator_expansion_pass;
    end if;

    foreach anchor in array p_anchors loop
        total := coalesce(array_length(generated, 1), 0);
        if total = 0 or anchor.start_time >= (generated[total]).end_time then continue; end if;
        if anchor.duration_minutes is null then
            raise exception 'Anchor % has no duration for this date', anchor.name using errcode = '22023';
        end if;

        duration := case when anchor.id = p_naseehah_anchor_id
            then p_prayer_minutes + p_naseehah_minutes else anchor.duration_minutes end;
        anchor_name := case when anchor.id = p_naseehah_anchor_id
            then anchor.name || ' + Naseehah' else anchor.name end;
        containing := null;
        insertion := null;
        for index in 1..total loop
            if anchor.start_time > (generated[index]).start_time
               and anchor.start_time < (generated[index]).end_time then
                containing := index;
                exit;
            end if;
        end loop;
        if containing is null then
            for index in 1..total loop
                if (generated[index]).start_time >= anchor.start_time then
                    insertion := index;
                    exit;
                end if;
            end loop;
            if insertion is null then continue; end if;
        end if;

        if containing is not null then
            source := generated[containing];
            before_periods := case when containing > 1
                then generated[1:containing - 1] else '{}'::private.generated_period[] end;
            after_periods := case when containing < total
                then generated[containing + 1:total] else '{}'::private.generated_period[] end;
            generated := before_periods || array[
                row(source.id, source.name || ' (part 1)', source.start_time,
                    anchor.start_time, source.is_lesson)::private.generated_period,
                row(private.generator_stable_id(p_timetable_id,
                        'anchor:' || replace(lower(anchor.id::text), '-', '')),
                    anchor_name, anchor.start_time,
                    private.generator_add_minutes(anchor.start_time, duration), false)::private.generated_period,
                row(private.generator_stable_id(p_timetable_id,
                        'period:' || replace(lower(source.id::text), '-', '') || ':part:2'),
                    source.name || ' (part 2)', private.generator_add_minutes(anchor.start_time, duration),
                    private.generator_add_minutes(source.end_time, duration), source.is_lesson)::private.generated_period
            ] || after_periods;
            insertion := containing + 3;
        else
            before_periods := case when insertion > 1
                then generated[1:insertion - 1] else '{}'::private.generated_period[] end;
            after_periods := generated[insertion:total];
            generated := before_periods || array[
                row(private.generator_stable_id(p_timetable_id,
                        'anchor:' || replace(lower(anchor.id::text), '-', '')),
                    anchor_name, anchor.start_time,
                    private.generator_add_minutes(anchor.start_time, duration), false)::private.generated_period
            ] || after_periods;
            insertion := insertion + 1;
        end if;

        total := array_length(generated, 1);
        if insertion <= total then
            for index in insertion..total loop
                generated[index] := row(
                    (generated[index]).id, (generated[index]).name,
                    private.generator_add_minutes((generated[index]).start_time, duration),
                    private.generator_add_minutes((generated[index]).end_time, duration),
                    (generated[index]).is_lesson)::private.generated_period;
            end loop;
        end if;
        applied := array_append(applied, anchor.id);
    end loop;
    return row(generated, applied)::private.generator_expansion_pass;
end;
$$;

create function private.expand_timetable_shape(
    p_timetable_id uuid,
    p_org_id uuid,
    p_day_start time,
    p_lesson_count integer,
    p_lesson_minutes integer,
    p_break_after_lesson integer,
    p_break_minutes integer,
    p_adjusts_for_prayer boolean,
    p_date date
)
returns private.generated_period[]
language plpgsql security definer set search_path = '' as $$
declare
    authored private.generated_period[] := '{}'::private.generated_period[];
    anchors private.resolved_generator_anchor[] := '{}'::private.resolved_generator_anchor[];
    cursor_time time := p_day_start;
    end_time time;
    lesson_number integer;
    before_count integer := coalesce(p_break_after_lesson, p_lesson_count);
    before_block uuid := private.generator_stable_id(p_timetable_id, 'shape:lessons:before');
    break_block uuid := private.generator_stable_id(p_timetable_id, 'shape:break');
    after_block uuid := private.generator_stable_id(p_timetable_id, 'shape:lessons:after');
    baseline private.generator_expansion_pass;
    final_pass private.generator_expansion_pass;
    naseehah_anchor_id uuid;
    prayer_minutes constant integer := 10;
    naseehah_minutes constant integer := 15;
begin
    if p_lesson_count not between 1 and 20 then
        raise exception 'Lesson count must be between 1 and 20' using errcode = '22023';
    end if;
    if p_lesson_minutes not between 5 and 120 then
        raise exception 'Lesson length must be between 5 and 120 minutes' using errcode = '22023';
    end if;
    if (p_break_after_lesson is null) <> (p_break_minutes is null)
       or (p_break_after_lesson is not null and
           (p_break_after_lesson not between 1 and p_lesson_count - 1
            or p_break_minutes not between 5 and 120)) then
        raise exception 'Break position and length are invalid' using errcode = '22023';
    end if;

    lesson_number := 0;
    for slot in 0..before_count - 1 loop
        lesson_number := lesson_number + 1;
        end_time := private.generator_add_minutes(cursor_time, p_lesson_minutes);
        authored := array_append(authored, row(
            private.generator_stable_id(p_timetable_id,
                'block:' || replace(lower(before_block::text), '-', '') || ':slot:' || slot),
            'Lesson ' || lesson_number, cursor_time, end_time, true)::private.generated_period);
        cursor_time := end_time;
    end loop;

    if p_break_after_lesson is not null then
        end_time := private.generator_add_minutes(cursor_time, p_break_minutes);
        authored := array_append(authored, row(
            private.generator_stable_id(p_timetable_id,
                'block:' || replace(lower(break_block::text), '-', '') || ':slot:0'),
            case when p_adjusts_for_prayer and p_day_start < time '15:00'
                 then 'Break / Naseehah' else 'Break' end,
            cursor_time, end_time, false)::private.generated_period);
        cursor_time := end_time;

        for slot in 0..(p_lesson_count - p_break_after_lesson) - 1 loop
            lesson_number := lesson_number + 1;
            end_time := private.generator_add_minutes(cursor_time, p_lesson_minutes);
            authored := array_append(authored, row(
                private.generator_stable_id(p_timetable_id,
                    'block:' || replace(lower(after_block::text), '-', '') || ':slot:' || slot),
                'Lesson ' || lesson_number, cursor_time, end_time, true)::private.generated_period);
            cursor_time := end_time;
        end loop;
    end if;

    if not p_adjusts_for_prayer then return authored; end if;

    select coalesce(array_agg(row(
        resolved.id, resolved.key, resolved.name, resolved.start_time, resolved.duration_minutes
    )::private.resolved_generator_anchor
    order by resolved.start_time, private.generator_guid_sort_key(resolved.id)),
    '{}'::private.resolved_generator_anchor[])
    into anchors
    from (
        select anchor.id, anchor.key, anchor.name,
            case when date_override.id is not null then date_override.start_time
                 else coalesce(weekday_standing.start_time, default_standing.start_time) end start_time,
            case when date_override.id is not null
                 then date_override.duration_minutes
                 when weekday_standing.start_time is not null
                 then weekday_standing.duration_minutes
                 else default_standing.duration_minutes end duration_minutes
        from public.organization_anchors anchor
        left join public.anchor_date_overrides date_override
          on date_override.anchor_id = anchor.id and date_override.date = p_date
        left join lateral (
            select standing.start_time, standing.duration_minutes
            from public.anchor_standing_times standing
            where standing.anchor_id = anchor.id
              and standing.weekday = extract(isodow from p_date)::smallint - 1
              and standing.effective_from <= p_date
            order by standing.effective_from desc, standing.id
            limit 1
        ) weekday_standing on date_override.id is null
        left join lateral (
            select standing.start_time, standing.duration_minutes
            from public.anchor_standing_times standing
            where standing.anchor_id = anchor.id and standing.weekday is null
              and standing.effective_from <= p_date
            order by standing.effective_from desc, standing.id
            limit 1
        ) default_standing on date_override.id is null
        where anchor.org_id = p_org_id and not coalesce(date_override.is_cancelled, false)
    ) resolved
    where resolved.start_time is not null and resolved.start_time >= p_day_start;

    baseline := private.apply_generator_anchors(
        p_timetable_id, authored, anchors, null, prayer_minutes, naseehah_minutes);
    if p_day_start >= time '15:00' and coalesce(array_length(baseline.applied_anchor_ids, 1), 0) > 0 then
        select anchor.id into naseehah_anchor_id
        from unnest(anchors) anchor
        where anchor.id = any(baseline.applied_anchor_ids)
        order by abs(extract(epoch from (anchor.start_time - time '19:00')) / 60),
                 anchor.start_time, private.generator_guid_sort_key(anchor.id)
        limit 1;
    end if;
    final_pass := private.apply_generator_anchors(
        p_timetable_id, authored, anchors, naseehah_anchor_id, prayer_minutes, naseehah_minutes);
    return final_pass.periods;
end;
$$;

create function private.expand_generated_timetable(p_timetable_id uuid, p_date date)
returns private.generated_period[]
language plpgsql security definer set search_path = '' as $$
declare
    generator public.timetable_generators%rowtype;
begin
    select * into strict generator from public.timetable_generators
    where timetable_id = p_timetable_id;
    return private.expand_timetable_shape(
        generator.timetable_id, generator.org_id, generator.day_start,
        generator.lesson_count, generator.lesson_minutes,
        generator.break_after_lesson, generator.break_minutes,
        generator.adjusts_for_prayer, p_date);
end;
$$;

create function private.generated_periods_json(
    p_timetable_id uuid, p_periods private.generated_period[])
returns jsonb language sql stable security invoker set search_path = '' as $$
    select coalesce(jsonb_agg(jsonb_build_object(
        'id', period.id, 'timetable_id', p_timetable_id, 'name', period.name,
        'start_time', period.start_time, 'end_time', period.end_time,
        'sort_order', period.ordinality::integer - 1, 'is_lesson', period.is_lesson
    ) order by period.ordinality), '[]'::jsonb)
    from unnest(p_periods) with ordinality
        period(id, name, start_time, end_time, is_lesson, ordinality);
$$;

revoke all on function private.generator_stable_id(uuid, text) from public, anon, authenticated;
revoke all on function private.generator_add_minutes(time, integer) from public, anon, authenticated;
revoke all on function private.generator_guid_sort_key(uuid) from public, anon, authenticated;
revoke all on function private.apply_generator_anchors(uuid, private.generated_period[], private.resolved_generator_anchor[], uuid, integer, integer) from public, anon, authenticated;
revoke all on function private.expand_timetable_shape(uuid, uuid, time, integer, integer, integer, integer, boolean, date) from public, anon, authenticated;
revoke all on function private.expand_generated_timetable(uuid, date) from public, anon, authenticated;
revoke all on function private.generated_periods_json(uuid, private.generated_period[]) from public, anon, authenticated;

create function public.admin_preview_generated_timetable(
    p_timetable_id uuid, p_shape jsonb, p_date date
)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    organization_today date;
    expanded private.generated_period[];
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    -- Preview is also used before a new timetable has been saved. A colliding id
    -- from another organisation is denied; an unused id is safe because this RPC
    -- is pure and writes nothing.
    if exists (select 1 from public.timetables timetable
               where timetable.id = p_timetable_id and timetable.org_id <> caller_org) then
        raise exception 'The timetable belongs to another organization' using errcode = '42501';
    end if;
    select timezone(organization.timezone, now())::date into strict organization_today
    from public.organizations organization where organization.id = caller_org;
    if p_date is null or p_date < organization_today or p_date > organization_today + 366 then
        raise exception 'Preview date must be between today and 366 days from today' using errcode = '22023';
    end if;

    expanded := private.expand_timetable_shape(
        p_timetable_id, caller_org, (p_shape ->> 'day_start')::time,
        (p_shape ->> 'lesson_count')::integer, (p_shape ->> 'lesson_minutes')::integer,
        nullif(p_shape ->> 'break_after_lesson', '')::integer,
        nullif(p_shape ->> 'break_minutes', '')::integer,
        coalesce((p_shape ->> 'adjusts_for_prayer')::boolean, true), p_date);
    return jsonb_build_object('date', p_date,
        'periods', private.generated_periods_json(p_timetable_id, expanded));
end;
$$;

create function public.admin_save_generated_timetable(p_timetable_id uuid, p_shape jsonb)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_date date;
    expanded private.generated_period[];
    original_periods jsonb;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if not exists (select 1 from public.timetables timetable
                   where timetable.id = p_timetable_id and timetable.org_id = caller_org) then
        raise exception 'The timetable does not belong to your organization' using errcode = '42501';
    end if;
    perform pg_advisory_xact_lock(hashtextextended('aqi.generator:' || p_timetable_id::text, 0));
    select timezone(organization.timezone, now())::date into strict target_date
    from public.organizations organization where organization.id = caller_org;

    expanded := private.expand_timetable_shape(
        p_timetable_id, caller_org, (p_shape ->> 'day_start')::time,
        (p_shape ->> 'lesson_count')::integer, (p_shape ->> 'lesson_minutes')::integer,
        nullif(p_shape ->> 'break_after_lesson', '')::integer,
        nullif(p_shape ->> 'break_minutes', '')::integer,
        coalesce((p_shape ->> 'adjusts_for_prayer')::boolean, true), target_date);

    select coalesce(jsonb_agg(to_jsonb(period) order by period.sort_order), '[]'::jsonb)
    into original_periods from public.periods period where period.timetable_id = p_timetable_id;

    insert into public.timetable_generators (
        timetable_id, org_id, day_start, lesson_count, lesson_minutes,
        break_after_lesson, break_minutes, adjusts_for_prayer, pre_conversion_periods)
    values (
        p_timetable_id, caller_org, (p_shape ->> 'day_start')::time,
        (p_shape ->> 'lesson_count')::integer, (p_shape ->> 'lesson_minutes')::integer,
        nullif(p_shape ->> 'break_after_lesson', '')::integer,
        nullif(p_shape ->> 'break_minutes', '')::integer,
        coalesce((p_shape ->> 'adjusts_for_prayer')::boolean, true), original_periods)
    on conflict (timetable_id) do update set
        day_start = excluded.day_start, lesson_count = excluded.lesson_count,
        lesson_minutes = excluded.lesson_minutes,
        break_after_lesson = excluded.break_after_lesson,
        break_minutes = excluded.break_minutes,
        adjusts_for_prayer = excluded.adjusts_for_prayer;

    update public.timetables set is_generated = true where id = p_timetable_id;
    perform set_config('aqi.generator_write', 'on', true);
    set constraints public.periods_timetable_name_key, public.periods_timetable_sort_order_key deferred;
    delete from public.periods where timetable_id = p_timetable_id;
    insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select desired.id, p_timetable_id, desired.name, desired.start_time, desired.end_time,
           desired.ordinality::integer - 1, desired.is_lesson
    from unnest(expanded) with ordinality
        desired(id, name, start_time, end_time, is_lesson, ordinality);
    return private.generated_periods_json(p_timetable_id, expanded);
end;
$$;

create function public.admin_disable_generated_timetable(p_timetable_id uuid)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    restore_rows jsonb;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    perform pg_advisory_xact_lock(hashtextextended('aqi.generator:' || p_timetable_id::text, 0));
    select generator.pre_conversion_periods into restore_rows
    from public.timetable_generators generator
    join public.timetables timetable on timetable.id = generator.timetable_id
    where generator.timetable_id = p_timetable_id and generator.org_id = caller_org
      and timetable.is_generated;
    if not found then
        raise exception 'The timetable is not generated or does not belong to your organization' using errcode = '42501';
    end if;
    if jsonb_typeof(restore_rows) <> 'array' then
        raise exception 'The saved manual timetable is invalid' using errcode = '22023';
    end if;

    perform set_config('aqi.generator_write', 'on', true);
    set constraints public.periods_timetable_name_key, public.periods_timetable_sort_order_key deferred;
    delete from public.periods where timetable_id = p_timetable_id;
    insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select restored.id, p_timetable_id, restored.name, restored.start_time,
           restored.end_time, restored.sort_order, restored.is_lesson
    from jsonb_to_recordset(restore_rows) restored(
        id uuid, timetable_id uuid, name text, start_time time,
        end_time time, sort_order integer, is_lesson boolean);
    update public.timetables set is_generated = false where id = p_timetable_id;
    delete from public.timetable_generators where timetable_id = p_timetable_id;
    return coalesce((select jsonb_agg(to_jsonb(period) order by period.sort_order)
                     from public.periods period where period.timetable_id = p_timetable_id), '[]'::jsonb);
end;
$$;

create function public.admin_bulk_upsert_anchor_date_overrides(p_anchor_id uuid, p_rows jsonb)
returns integer language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    affected integer;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if not exists (select 1 from public.organization_anchors anchor
                   where anchor.id = p_anchor_id and anchor.org_id = caller_org
                     and anchor.key = 'maghrib') then
        raise exception 'The Maghrib anchor does not belong to your organization' using errcode = '42501';
    end if;
    if jsonb_typeof(p_rows) <> 'array' then
        raise exception 'Rows must be an array' using errcode = '22023';
    end if;
    if exists (select 1 from jsonb_to_recordset(p_rows) supplied(date date)
               group by supplied.date having count(*) > 1) then
        raise exception 'Each date may appear only once' using errcode = '23505';
    end if;
    insert into public.anchor_date_overrides
        (org_id, anchor_id, date, start_time, duration_minutes, is_cancelled)
    select caller_org, p_anchor_id, supplied.date, supplied.start_time,
           supplied.duration_minutes, coalesce(supplied.is_cancelled, false)
    from jsonb_to_recordset(p_rows) supplied(
        date date, start_time time, duration_minutes integer, is_cancelled boolean)
    on conflict (anchor_id, date) do update set start_time = excluded.start_time,
        duration_minutes = excluded.duration_minutes, is_cancelled = excluded.is_cancelled;
    get diagnostics affected = row_count;
    return affected;
end;
$$;

create function public.admin_save_prayer_fixed_times(p_rows jsonb, p_effective_from date)
returns integer language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    organization_today date;
    supplied record;
    target_anchor uuid;
    affected integer := 0;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    select timezone(organization.timezone, now())::date into strict organization_today
    from public.organizations organization where organization.id = caller_org;
    if p_effective_from < organization_today then
        raise exception 'Effective date cannot be in the past' using errcode = '22023';
    end if;
    if jsonb_typeof(p_rows) <> 'array' or jsonb_array_length(p_rows) <> 3 then
        raise exception 'Supply Asr, Isha and Friday Jumu''ah exactly once' using errcode = '22023';
    end if;
    if exists (
        select 1 from jsonb_to_recordset(p_rows) row(anchor_key text, weekday smallint)
        where not ((row.anchor_key in ('asr', 'isha') and row.weekday is null)
                   or (row.anchor_key = 'zuhr' and row.weekday = 4))
    ) or (select count(distinct row.anchor_key || ':' || coalesce(row.weekday::text, 'default'))
          from jsonb_to_recordset(p_rows) row(anchor_key text, weekday smallint)) <> 3 then
        raise exception 'Supply Asr, Isha and Friday Jumu''ah exactly once' using errcode = '22023';
    end if;

    for supplied in select * from jsonb_to_recordset(p_rows)
        as row(anchor_key text, start_time time, duration_minutes integer, weekday smallint)
    loop
        if supplied.start_time is null
           or (supplied.weekday is null and supplied.duration_minutes is null)
           or supplied.duration_minutes is not null and supplied.duration_minutes not between 1 and 120 then
            raise exception 'Prayer times or durations are invalid' using errcode = '22023';
        end if;
        select anchor.id into strict target_anchor from public.organization_anchors anchor
        where anchor.org_id = caller_org and anchor.key = supplied.anchor_key;
        if supplied.weekday is null then
            insert into public.anchor_standing_times
                (org_id, anchor_id, weekday, start_time, duration_minutes, effective_from)
            values (caller_org, target_anchor, null, supplied.start_time,
                    supplied.duration_minutes, p_effective_from)
            on conflict (anchor_id, effective_from) where weekday is null
            do update set start_time = excluded.start_time, duration_minutes = excluded.duration_minutes;
        else
            insert into public.anchor_standing_times
                (org_id, anchor_id, weekday, start_time, duration_minutes, effective_from)
            values (caller_org, target_anchor, supplied.weekday, supplied.start_time,
                    supplied.duration_minutes, p_effective_from)
            on conflict (anchor_id, weekday, effective_from) where weekday is not null
            do update set start_time = excluded.start_time, duration_minutes = excluded.duration_minutes;
        end if;
        affected := affected + 1;
    end loop;
    return affected;
end;
$$;

create function private.generator_maintenance(p_org_id uuid)
returns public.generator_maintenance_runs
language plpgsql security definer set search_path = '' as $$
declare
    started timestamptz := clock_timestamp();
    organization_timezone text;
    target_date date;
    generated record;
    expanded private.generated_period[];
    stored private.generated_period[];
    stored_sort_orders_match boolean;
    written integer := 0;
    failures text[] := '{}'::text[];
    failure_message text;
    result public.generator_maintenance_runs;
begin
    select organization.timezone into strict organization_timezone
    from public.organizations organization where organization.id = p_org_id;
    target_date := timezone(organization_timezone, now())::date;
    perform pg_advisory_xact_lock(hashtextextended('aqi.generator_maintenance:' || p_org_id::text, 0));
    perform set_config('aqi.generator_write', 'on', true);

    for generated in
        select generator.timetable_id from public.timetable_generators generator
        join public.timetables timetable on timetable.id = generator.timetable_id
        where generator.org_id = p_org_id and timetable.is_generated
        order by generator.timetable_id
    loop
        begin
            expanded := private.expand_generated_timetable(generated.timetable_id, target_date);
            select coalesce(array_agg(row(period.id, period.name, period.start_time,
                       period.end_time, period.is_lesson)::private.generated_period
                       order by period.sort_order), '{}'::private.generated_period[]),
                   coalesce(bool_and(period.sort_order = period.expected_sort_order), true)
            into stored, stored_sort_orders_match
            from (
                select existing.*,
                       row_number() over (order by existing.sort_order)::integer - 1 expected_sort_order
                from public.periods existing where existing.timetable_id = generated.timetable_id
            ) period;
            if stored is not distinct from expanded and stored_sort_orders_match then continue; end if;

            set constraints public.periods_timetable_name_key, public.periods_timetable_sort_order_key deferred;
            delete from public.periods where timetable_id = generated.timetable_id;
            insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
            select desired.id, generated.timetable_id, desired.name, desired.start_time,
                   desired.end_time, desired.ordinality::integer - 1, desired.is_lesson
            from unnest(expanded) with ordinality
                desired(id, name, start_time, end_time, is_lesson, ordinality);
            written := written + 1;
        exception when others then
            get stacked diagnostics failure_message = message_text;
            failures := array_append(failures,
                format('Timetable %s: %s', generated.timetable_id, failure_message));
        end;
    end loop;

    insert into public.generator_maintenance_runs
        (org_id, started_at, duration_ms, regenerated_date, timetables_written, error)
    values (p_org_id, started,
        greatest(0, floor(extract(epoch from (clock_timestamp() - started)) * 1000)::bigint),
        target_date, written, nullif(array_to_string(failures, E'\n'), ''))
    on conflict (org_id, regenerated_date) do update set
        started_at = excluded.started_at, duration_ms = excluded.duration_ms,
        timetables_written = excluded.timetables_written, error = excluded.error
    where excluded.timetables_written > 0
       or public.generator_maintenance_runs.error is distinct from excluded.error
    returning * into result;
    if result.id is null then
        select * into strict result from public.generator_maintenance_runs
        where org_id = p_org_id and regenerated_date = target_date;
    end if;
    return result;
end;
$$;

create function public.run_generator_maintenance(p_org_id uuid)
returns public.generator_maintenance_runs
language sql security definer set search_path = ''
as $$ select private.generator_maintenance(p_org_id); $$;

create function public.admin_regenerate_generated_timetables()
returns public.generator_maintenance_runs
language plpgsql security definer set search_path = '' as $$
declare caller_org uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    return private.generator_maintenance(caller_org);
end;
$$;

revoke all on function public.admin_preview_generated_timetable(uuid, jsonb, date) from public, anon, service_role;
revoke all on function public.admin_save_generated_timetable(uuid, jsonb) from public, anon, service_role;
revoke all on function public.admin_disable_generated_timetable(uuid) from public, anon, service_role;
revoke all on function public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb) from public, anon, service_role;
revoke all on function public.admin_save_prayer_fixed_times(jsonb, date) from public, anon, service_role;
revoke all on function private.generator_maintenance(uuid) from public, anon, authenticated, service_role;
revoke all on function public.run_generator_maintenance(uuid) from public, anon, authenticated;
revoke all on function public.admin_regenerate_generated_timetables() from public, anon, service_role;

grant execute on function public.admin_preview_generated_timetable(uuid, jsonb, date) to authenticated;
grant execute on function public.admin_save_generated_timetable(uuid, jsonb) to authenticated;
grant execute on function public.admin_disable_generated_timetable(uuid) to authenticated;
grant execute on function public.admin_bulk_upsert_anchor_date_overrides(uuid, jsonb) to authenticated;
grant execute on function public.admin_save_prayer_fixed_times(jsonb, date) to authenticated;
grant execute on function public.run_generator_maintenance(uuid) to service_role;
grant execute on function public.admin_regenerate_generated_timetables() to authenticated;

revoke all on all tables in schema public from anon;
