alter table public.week_schedule add column audience_class_id uuid null references public.classes(id) on delete restrict;
alter table public.week_schedule drop constraint week_schedule_org_weekday_key;
alter table public.week_schedule add constraint week_schedule_org_weekday_audience_key unique nulls not distinct (org_id, weekday, audience_class_id);
create index week_schedule_audience_class_idx on public.week_schedule(audience_class_id) where audience_class_id is not null;

create or replace function private.seed_week_schedule() returns trigger language plpgsql security definer set search_path = '' as $$
begin
 insert into public.week_schedule(org_id,weekday,audience_class_id,timetable_id) select new.id,weekday.n,null,null from generate_series(0,6) weekday(n)
 on conflict on constraint week_schedule_org_weekday_audience_key do nothing;
 return new;
end; $$;

insert into public.week_schedule(org_id,weekday,audience_class_id,timetable_id)
select o.id,weekday.n,null,null from public.organizations o cross join generate_series(0,6) weekday(n)
on conflict on constraint week_schedule_org_weekday_audience_key do nothing;

drop policy week_schedule_insert_admin on public.week_schedule;
create policy week_schedule_insert_admin on public.week_schedule for insert to authenticated with check (
 (select private.is_admin()) and org_id=(select private.current_org_id()) and
 (audience_class_id is null or exists(select 1 from public.classes c where c.id=audience_class_id and c.org_id=(select private.current_org_id()))));
drop policy week_schedule_update_admin on public.week_schedule;
create policy week_schedule_update_admin on public.week_schedule for update to authenticated
using ((select private.is_admin()) and org_id=(select private.current_org_id())) with check (
 (select private.is_admin()) and org_id=(select private.current_org_id()) and
 (audience_class_id is null or exists(select 1 from public.classes c where c.id=audience_class_id and c.org_id=(select private.current_org_id()))));

create or replace function public.admin_save_week_schedule(p_weekday smallint,p_timetable_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare caller_org uuid;
begin
 if not coalesce((select private.is_admin()),false) then raise exception 'Administrator access is required' using errcode='42501'; end if;
 if p_weekday not between 0 and 6 then raise exception 'Invalid weekday' using errcode='22023'; end if;
 caller_org := (select private.current_org_id());
 if p_timetable_id is not null and not exists(select 1 from public.timetables t where t.id=p_timetable_id and t.org_id=caller_org) then raise exception 'The timetable belongs to another organization' using errcode='42501'; end if;
 insert into public.week_schedule(org_id,weekday,audience_class_id,timetable_id) values(caller_org,p_weekday,null,p_timetable_id)
 on conflict on constraint week_schedule_org_weekday_audience_key do update set timetable_id=excluded.timetable_id;
end; $$;

create function public.admin_save_week_schedule(p_weekday smallint,p_audience_class_id uuid,p_timetable_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare caller_org uuid;
begin
 if not coalesce((select private.is_admin()),false) then raise exception 'Administrator access is required' using errcode='42501'; end if;
 if p_weekday not between 0 and 6 then raise exception 'Invalid weekday' using errcode='22023'; end if;
 caller_org := (select private.current_org_id());
 if p_audience_class_id is not null and not exists(select 1 from public.classes c where c.id=p_audience_class_id and c.org_id=caller_org) then raise exception 'The class belongs to another organization' using errcode='42501'; end if;
 if p_timetable_id is not null and not exists(select 1 from public.timetables t where t.id=p_timetable_id and t.org_id=caller_org) then raise exception 'The timetable belongs to another organization' using errcode='42501'; end if;
 insert into public.week_schedule(org_id,weekday,audience_class_id,timetable_id) values(caller_org,p_weekday,p_audience_class_id,p_timetable_id)
 on conflict on constraint week_schedule_org_weekday_audience_key do update set timetable_id=excluded.timetable_id;
end; $$;

create function public.admin_delete_week_schedule(p_weekday smallint,p_audience_class_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare caller_org uuid;
begin
 if not coalesce((select private.is_admin()),false) then raise exception 'Administrator access is required' using errcode='42501'; end if;
 if p_audience_class_id is null then raise exception 'The default week schedule row cannot be deleted' using errcode='23514'; end if;
 caller_org := (select private.current_org_id());
 delete from public.week_schedule where org_id=caller_org and weekday=p_weekday and audience_class_id=p_audience_class_id;
end; $$;

revoke all on function public.admin_save_week_schedule(smallint,uuid) from public,anon;
revoke all on function public.admin_save_week_schedule(smallint,uuid,uuid) from public,anon;
revoke all on function public.admin_delete_week_schedule(smallint,uuid) from public,anon;
grant execute on function public.admin_save_week_schedule(smallint,uuid) to authenticated;
grant execute on function public.admin_save_week_schedule(smallint,uuid,uuid) to authenticated;
grant execute on function public.admin_delete_week_schedule(smallint,uuid) to authenticated;
