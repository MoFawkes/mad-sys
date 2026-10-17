-- Include prayer/Naseehah already underway at a class session start.
create or replace function private.apply_generator_anchors(
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
    elapsed integer;
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
        -- Reserve only the unelapsed portion of an opening prayer/Naseehah block.
        elapsed := greatest(0, extract(epoch from ((p_authored[1]).start_time - anchor.start_time))::integer / 60);
        if elapsed >= duration then
            -- Keep this overlapping candidate eligible for PM host selection in pass one.
            applied := array_append(applied, anchor.id);
            continue;
        end if;
        duration := duration - elapsed;
        if elapsed > 0 then anchor.start_time := (p_authored[1]).start_time; end if;
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

create or replace function private.expand_timetable_shape(
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
    where resolved.start_time is not null
      and (resolved.start_time >= p_day_start
           or resolved.start_time + make_interval(mins =>
               case when p_day_start >= time '15:00' then prayer_minutes + naseehah_minutes
                    else resolved.duration_minutes end) > p_day_start);

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

