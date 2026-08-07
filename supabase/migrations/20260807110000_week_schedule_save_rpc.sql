create function public.admin_save_week_schedule(p_weekday smallint, p_timetable_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    caller_org uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    if p_weekday < 0 or p_weekday > 6 then
        raise exception 'Weekday must be between 0 and 6' using errcode = '22023';
    end if;
    caller_org := (select private.current_org_id());
    if p_timetable_id is not null and not exists (
        select 1
        from public.timetables timetable
        where timetable.id = p_timetable_id
          and timetable.org_id = caller_org
    ) then
        raise exception 'The timetable belongs to another organization' using errcode = '42501';
    end if;
    insert into public.week_schedule (org_id, weekday, timetable_id)
    values (caller_org, p_weekday, p_timetable_id)
    on conflict on constraint week_schedule_org_weekday_key
    do update set timetable_id = excluded.timetable_id;
end;
$$;

revoke all on function public.admin_save_week_schedule(smallint, uuid) from public, anon;
grant execute on function public.admin_save_week_schedule(smallint, uuid) to authenticated;
