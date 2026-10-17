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
        -- PM sessions reserve the full opening prayer/Naseehah block.
        elapsed := case when anchor.id = p_naseehah_anchor_id then 0
            else greatest(0, extract(epoch from ((p_authored[1]).start_time - anchor.start_time))::integer / 60) end;
        if elapsed >= duration then
            -- Keep this overlapping candidate eligible for PM host selection in pass one.
            applied := array_append(applied, anchor.id);
            continue;
        end if;
        duration := duration - elapsed;
        if anchor.start_time < (p_authored[1]).start_time then
            anchor.start_time := (p_authored[1]).start_time;
        end if;
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

