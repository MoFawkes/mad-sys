create type private.generated_period as (
    id uuid,
    name text,
    start_time time,
    end_time time,
    is_lesson boolean
);

create type private.resolved_generator_anchor as (
    id uuid,
    key text,
    name text,
    start_time time,
    duration_minutes integer
);

create type private.generator_expansion_pass as (
    periods private.generated_period[],
    applied_anchor_ids uuid[]
);

create function private.generator_stable_id(p_timetable_id uuid, p_identity text)
returns uuid language plpgsql immutable strict security invoker set search_path = '' as $$
declare
    hash text := encode(extensions.digest(replace(lower(p_timetable_id::text), '-', '') || ':' || p_identity, 'sha256'), 'hex');
begin
    -- Match new Guid(SHA256[..16]) in .NET, whose first three fields use
    -- little-endian byte order when formatted as text.
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
returns bytea language plpgsql immutable strict security invoker set search_path = '' as $$
declare
    key bytea := decode(replace(lower(p_id::text), '-', ''), 'hex');
begin
    -- PostgreSQL's canonical UUID byte order already matches Guid.CompareTo.
    -- Keep the identity helper explicit so the high-bit parity test pins that
    -- fact and prevents a future byte swap or signed-field reinterpretation.
    return key;
end;
$$;

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
    index integer;
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
        if total = 0 or anchor.start_time >= (generated[total]).end_time then
            continue;
        end if;
        if anchor.duration_minutes is null then
            raise exception 'Anchor % has no duration for this date', anchor.name using errcode = '22023';
        end if;

        duration := case when anchor.id = p_naseehah_anchor_id
            then p_prayer_minutes + p_naseehah_minutes else anchor.duration_minutes end;
        anchor_name := case when anchor.id = p_naseehah_anchor_id then anchor.name || ' + Naseehah' else anchor.name end;
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
            if insertion is null then
                continue;
            end if;
        end if;

        if containing is not null then
            source := generated[containing];
            before_periods := case when containing > 1 then generated[1:containing - 1] else '{}'::private.generated_period[] end;
            after_periods := case when containing < total then generated[containing + 1:total] else '{}'::private.generated_period[] end;
            generated := before_periods || array[
                row(source.id, source.name || ' (part 1)', source.start_time, anchor.start_time, source.is_lesson)::private.generated_period,
                row(private.generator_stable_id(p_timetable_id, 'anchor:' || replace(lower(anchor.id::text), '-', '')), anchor_name,
                    anchor.start_time, private.generator_add_minutes(anchor.start_time, duration), false)::private.generated_period,
                row(private.generator_stable_id(p_timetable_id, 'period:' || replace(lower(source.id::text), '-', '') || ':part:2'),
                    source.name || ' (part 2)', private.generator_add_minutes(anchor.start_time, duration),
                    private.generator_add_minutes(source.end_time, duration), source.is_lesson)::private.generated_period
            ] || after_periods;
            insertion := containing + 3;
        else
            before_periods := case when insertion > 1 then generated[1:insertion - 1] else '{}'::private.generated_period[] end;
            after_periods := generated[insertion:total];
            generated := before_periods || array[
                row(private.generator_stable_id(p_timetable_id, 'anchor:' || replace(lower(anchor.id::text), '-', '')), anchor_name,
                    anchor.start_time, private.generator_add_minutes(anchor.start_time, duration), false)::private.generated_period
            ] || after_periods;
            insertion := insertion + 1;
        end if;

        total := array_length(generated, 1);
        if insertion <= total then
            for index in insertion..total loop
                generated[index] := row(
                    (generated[index]).id,
                    (generated[index]).name,
                    private.generator_add_minutes((generated[index]).start_time, duration),
                    private.generator_add_minutes((generated[index]).end_time, duration),
                    (generated[index]).is_lesson
                )::private.generated_period;
            end loop;
        end if;
        applied := array_append(applied, anchor.id);
    end loop;

    return row(generated, applied)::private.generator_expansion_pass;
end;
$$;

create function private.expand_generated_timetable(p_timetable_id uuid, p_date date)
returns private.generated_period[]
language plpgsql security definer set search_path = '' as $$
declare
    generator public.timetable_generators%rowtype;
    block public.timetable_generator_blocks%rowtype;
    authored private.generated_period[] := '{}'::private.generated_period[];
    anchors private.resolved_generator_anchor[] := '{}'::private.resolved_generator_anchor[];
    names text[] := '{}'::text[];
    cursor_time time;
    end_time time;
    requested_name text;
    unique_name text;
    count integer;
    slot integer;
    lesson_number integer := 0;
    suffix integer;
    baseline private.generator_expansion_pass;
    final_pass private.generator_expansion_pass;
    naseehah_anchor_id uuid;
    prayer_minutes constant integer := 10;
    naseehah_minutes constant integer := 15;
begin
    select * into strict generator
    from public.timetable_generators
    where timetable_id = p_timetable_id;
    cursor_time := generator.day_start;

    for block in
        select * from public.timetable_generator_blocks
        where timetable_id = p_timetable_id
        order by sort_order, id
    loop
        count := case when block.block_kind = 'lessons' then block.lesson_count else 1 end;
        for slot in 0..count - 1 loop
            end_time := private.generator_add_minutes(cursor_time,
                case when block.block_kind = 'lessons' then block.lesson_minutes else block.break_minutes end);
            if block.block_kind = 'lessons' then
                lesson_number := lesson_number + 1;
                requested_name := replace(generator.naming_pattern, '{number}', lesson_number::text);
            else
                requested_name := block.name;
                if block.hosts_naseehah and position('naseehah' in lower(requested_name)) = 0 then
                    requested_name := requested_name || ' / Naseehah';
                end if;
            end if;

            unique_name := requested_name;
            suffix := 2;
            while exists (select 1 from unnest(names) as existing(name) where lower(btrim(existing.name)) = lower(btrim(unique_name))) loop
                unique_name := requested_name || ' (' || suffix || ')';
                suffix := suffix + 1;
            end loop;
            names := array_append(names, unique_name);
            authored := array_append(authored, row(
                private.generator_stable_id(p_timetable_id,
                    'block:' || replace(lower(block.id::text), '-', '') || ':slot:' || slot),
                unique_name, cursor_time, end_time, block.block_kind = 'lessons'
            )::private.generated_period);
            cursor_time := end_time;
        end loop;
    end loop;

    select coalesce(array_agg(row(
        resolved.id, resolved.key, resolved.name, resolved.start_time, resolved.duration_minutes
    )::private.resolved_generator_anchor order by resolved.start_time, private.generator_guid_sort_key(resolved.id)), '{}'::private.resolved_generator_anchor[])
    into anchors
    from (
        select anchor.id, anchor.key, anchor.name,
            case when date_override.id is not null then date_override.start_time
                 else coalesce(weekday_standing.start_time, default_standing.start_time) end as start_time,
            case when date_override.id is not null then date_override.duration_minutes
                 else coalesce(weekday_standing.duration_minutes, default_standing.duration_minutes) end as duration_minutes
        from public.timetable_generator_anchors observed
        join public.organization_anchors anchor on anchor.id = observed.anchor_id
        left join public.anchor_date_overrides date_override
            on date_override.anchor_id = anchor.id and date_override.date = p_date
        left join lateral (
            select standing.start_time, standing.duration_minutes
            from public.anchor_standing_times standing
            where standing.anchor_id = anchor.id
              -- Database weekday convention is Monday=0 ... Sunday=6.
              and standing.weekday = (extract(isodow from p_date)::smallint - 1)
              and standing.effective_from <= p_date
            order by standing.effective_from desc, standing.id
            limit 1
        ) weekday_standing on date_override.id is null
        left join lateral (
            select standing.start_time, standing.duration_minutes
            from public.anchor_standing_times standing
            where standing.anchor_id = anchor.id
              and standing.weekday is null
              and standing.effective_from <= p_date
            order by standing.effective_from desc, standing.id
            limit 1
        ) default_standing on date_override.id is null and weekday_standing.start_time is null
        where observed.timetable_id = p_timetable_id
          and not coalesce(date_override.is_cancelled, false)
    ) resolved
    where resolved.start_time is not null and resolved.start_time >= generator.day_start;

    baseline := private.apply_generator_anchors(
        p_timetable_id, authored, anchors, null, prayer_minutes, naseehah_minutes);
    if generator.session_kind = 'pm' and coalesce(array_length(baseline.applied_anchor_ids, 1), 0) > 0 then
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

revoke all on function private.generator_stable_id(uuid, text) from public, anon, authenticated;
revoke all on function private.generator_add_minutes(time, integer) from public, anon, authenticated;
revoke all on function private.generator_guid_sort_key(uuid) from public, anon, authenticated;
revoke all on function private.apply_generator_anchors(uuid, private.generated_period[], private.resolved_generator_anchor[], uuid, integer, integer) from public, anon, authenticated;
revoke all on function private.expand_generated_timetable(uuid, date) from public, anon, authenticated;

create table public.generator_maintenance_runs (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id),
    started_at timestamptz not null,
    duration_ms bigint not null constraint generator_maintenance_runs_duration_check check (duration_ms >= 0),
    regenerated_date date not null,
    timetables_written integer not null constraint generator_maintenance_runs_written_check check (timetables_written >= 0),
    error text,
    created_at timestamptz not null default now(),
    constraint generator_maintenance_runs_org_date_key unique (org_id, regenerated_date)
);

alter table public.generator_maintenance_runs enable row level security;
revoke all on public.generator_maintenance_runs from public, anon, authenticated;
grant select on public.generator_maintenance_runs to authenticated;
create policy generator_maintenance_runs_select_admin on public.generator_maintenance_runs
for select to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create function private.generator_maintenance(p_org_id uuid)
returns public.generator_maintenance_runs
language plpgsql security definer set search_path = '' as $$
declare
    organization_timezone text;
    target_date date;
    started timestamptz := clock_timestamp();
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
    from public.organizations organization
    where organization.id = p_org_id;
    target_date := timezone(organization_timezone, now())::date;
    perform pg_advisory_xact_lock(hashtextextended('aqi.generator_maintenance:' || p_org_id::text, 0));
    perform set_config('aqi.generator_write', 'on', true);

    for generated in
        select generator.timetable_id
        from public.timetable_generators generator
        join public.timetables timetable on timetable.id = generator.timetable_id
        where generator.org_id = p_org_id and timetable.is_generated
        order by generator.timetable_id
    loop
        begin
            expanded := private.expand_generated_timetable(generated.timetable_id, target_date);
            select coalesce(array_agg(row(
                       period.id, period.name, period.start_time, period.end_time, period.is_lesson
                   )::private.generated_period order by period.sort_order), '{}'::private.generated_period[]),
                   coalesce(bool_and(period.sort_order = period.expected_sort_order), true)
            into stored, stored_sort_orders_match
            from (
                select existing.*, row_number() over (order by existing.sort_order)::integer - 1 as expected_sort_order
                from public.periods existing
                where existing.timetable_id = generated.timetable_id
            ) period;

            if stored is not distinct from expanded and stored_sort_orders_match then
                continue;
            end if;

            -- A deferred violation is raised at COMMIT, outside this per-timetable
            -- exception block. Current unique names and sequential ordinals make
            -- that unreachable; do not treat this block as covering commit failures.
            set constraints public.periods_timetable_name_key, public.periods_timetable_sort_order_key deferred;
            delete from public.periods period
            where period.timetable_id = generated.timetable_id
              and not exists (select 1 from unnest(expanded) desired where desired.id = period.id);

            insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
            select desired.id, generated.timetable_id, desired.name, desired.start_time,
                desired.end_time, desired.ordinality::integer - 1, desired.is_lesson
            from unnest(expanded) with ordinality desired(id, name, start_time, end_time, is_lesson, ordinality)
            on conflict (id) do update set
                timetable_id = excluded.timetable_id,
                name = excluded.name,
                start_time = excluded.start_time,
                end_time = excluded.end_time,
                sort_order = excluded.sort_order,
                is_lesson = excluded.is_lesson
            where (public.periods.timetable_id, public.periods.name, public.periods.start_time,
                   public.periods.end_time, public.periods.sort_order, public.periods.is_lesson)
                  is distinct from
                  (excluded.timetable_id, excluded.name, excluded.start_time,
                   excluded.end_time, excluded.sort_order, excluded.is_lesson);
            written := written + 1;
        exception when others then
            get stacked diagnostics failure_message = message_text;
            failures := array_append(failures,
                format('Timetable %s: %s', generated.timetable_id, failure_message));
        end;
    end loop;

    insert into public.generator_maintenance_runs (
        org_id, started_at, duration_ms, regenerated_date, timetables_written, error
    ) values (
        p_org_id, started,
        greatest(0, floor(extract(epoch from (clock_timestamp() - started)) * 1000)::bigint),
        target_date, written, nullif(array_to_string(failures, E'\n'), '')
    )
    on conflict (org_id, regenerated_date) do update set
        started_at = excluded.started_at,
        duration_ms = excluded.duration_ms,
        timetables_written = excluded.timetables_written,
        error = excluded.error
    -- A later same-day run may follow an anchor or definition edit. Record that
    -- meaningful regeneration, but leave the existing row byte-for-byte alone
    -- for the ordinary idempotent no-op path.
    where excluded.timetables_written > 0
       or public.generator_maintenance_runs.error is distinct from excluded.error
    returning * into result;

    if result.id is null then
        select * into strict result
        from public.generator_maintenance_runs
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
declare
    caller_org uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    return private.generator_maintenance(caller_org);
end;
$$;

revoke all on function private.generator_maintenance(uuid) from public, anon, authenticated, service_role;
revoke all on function public.run_generator_maintenance(uuid) from public, anon, authenticated;
grant execute on function public.run_generator_maintenance(uuid) to service_role;
revoke all on function public.admin_regenerate_generated_timetables() from public, anon, service_role;
grant execute on function public.admin_regenerate_generated_timetables() to authenticated;

-- Preserve the standing database-wide grant invariant for every new table.
revoke all on all tables in schema public from anon;
