alter table public.periods drop constraint periods_timetable_name_key;
alter table public.periods add constraint periods_timetable_name_key unique (timetable_id, name) deferrable initially immediate;
alter table public.periods drop constraint periods_timetable_sort_order_key;
alter table public.periods add constraint periods_timetable_sort_order_key unique (timetable_id, sort_order) deferrable initially immediate;

create function public.admin_save_timetable(p_timetable jsonb, p_periods jsonb)
returns void language plpgsql security definer set search_path = '' as $$
declare
    caller_org uuid;
    target_timetable_id uuid := (p_timetable ->> 'id')::uuid;
begin
    if not coalesce((select private.is_admin()), false) then
        raise exception 'Administrator access is required' using errcode = '42501';
    end if;
    caller_org := (select private.current_org_id());
    if exists (select 1 from public.timetables t where t.id = target_timetable_id and t.org_id <> caller_org) then
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
    values (target_timetable_id, caller_org, p_timetable ->> 'name', coalesce((p_timetable ->> 'is_archived')::boolean, false))
    on conflict (id) do update set name = excluded.name, is_archived = excluded.is_archived;
    delete from public.periods period
    where period.timetable_id = target_timetable_id
      and not exists (select 1 from jsonb_to_recordset(p_periods) supplied(id uuid) where supplied.id = period.id);
    insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order, is_lesson)
    select supplied.id, target_timetable_id, supplied.name, supplied.start_time, supplied.end_time, supplied.sort_order, supplied.is_lesson
    from jsonb_to_recordset(p_periods) supplied(id uuid, name text, start_time time, end_time time, sort_order integer, is_lesson boolean)
    on conflict (id) do update set timetable_id = excluded.timetable_id, name = excluded.name,
        start_time = excluded.start_time, end_time = excluded.end_time,
        sort_order = excluded.sort_order, is_lesson = excluded.is_lesson;
end;
$$;

create function private.seed_week_schedule()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
    insert into public.week_schedule (org_id, weekday, timetable_id)
    select new.id, weekday.n, null from generate_series(0, 6) weekday(n)
    on conflict (org_id, weekday) do nothing;
    return new;
end;
$$;
revoke all on function private.seed_week_schedule() from public, anon, authenticated;
create trigger organizations_seed_week_schedule after insert on public.organizations
for each row execute function private.seed_week_schedule();

insert into public.week_schedule (org_id, weekday, timetable_id)
select organization.id, weekday.n, null
from public.organizations organization cross join generate_series(0, 6) weekday(n)
on conflict (org_id, weekday) do nothing;

revoke all on function public.admin_save_timetable(jsonb, jsonb) from public, anon;
grant execute on function public.admin_save_timetable(jsonb, jsonb) to authenticated;
