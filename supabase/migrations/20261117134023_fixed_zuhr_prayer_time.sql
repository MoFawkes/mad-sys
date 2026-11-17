create or replace function public.admin_save_prayer_fixed_times(p_rows jsonb, p_effective_from date)
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
    if p_effective_from is null or p_effective_from < organization_today then
        raise exception 'Effective date cannot be in the past' using errcode = '22023';
    end if;
    if jsonb_typeof(p_rows) is distinct from 'array' then
        raise exception 'Supply fixed prayer times as an array' using errcode = '22023';
    end if;
    -- Older clients send the three required rows without a default Zuhr.
    if exists (
        select 1 from jsonb_to_recordset(p_rows) row(anchor_key text, weekday smallint)
        where ((row.anchor_key in ('asr', 'isha', 'zuhr') and row.weekday is null)
               or (row.anchor_key = 'zuhr' and row.weekday = 4)) is not true
    ) or (
        select count(distinct row.anchor_key || ':' || coalesce(row.weekday::text, 'default'))
               <> jsonb_array_length(p_rows)
            or count(distinct row.anchor_key) filter (where row.weekday is null and row.anchor_key in ('asr', 'isha')) <> 2
            or count(*) filter (where row.weekday = 4) <> 1
        from jsonb_to_recordset(p_rows) row(anchor_key text, weekday smallint)
    ) then
        raise exception 'Supply Asr, Isha and Friday Jumu''ah exactly once, with optional Zuhr'
            using errcode = '22023';
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
