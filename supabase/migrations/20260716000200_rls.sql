create function private.current_org_id()
returns uuid
language sql
stable
security definer
set search_path = ''
as $$
    select p.org_id
    from public.profiles as p
    where p.id = (select auth.uid())
      and p.is_active
$$;

create function private.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1
        from public.profiles as p
        where p.id = (select auth.uid())
          and p.role = 'admin'
          and p.is_active
    )
$$;

revoke all on function private.current_org_id() from public, anon, authenticated;
revoke all on function private.is_admin() from public, anon, authenticated;
grant usage on schema private to authenticated;
grant execute on function private.current_org_id() to authenticated;
grant execute on function private.is_admin() to authenticated;

revoke all on all tables in schema public from anon;
revoke all on all sequences in schema public from anon;

grant select on public.organizations to authenticated;
grant select, update on public.profiles to authenticated;
grant select, insert, update, delete on public.timetables to authenticated;
grant select, insert, update, delete on public.periods to authenticated;
grant select, insert, update, delete on public.week_schedule to authenticated;
grant select, insert, update, delete on public.date_overrides to authenticated;
grant select, insert, update, delete on public.announcements to authenticated;
grant select on public.audit_log to authenticated;

alter table public.organizations enable row level security;
alter table public.profiles enable row level security;
alter table public.timetables enable row level security;
alter table public.periods enable row level security;
alter table public.week_schedule enable row level security;
alter table public.date_overrides enable row level security;
alter table public.announcements enable row level security;
alter table public.audit_log enable row level security;

create policy organizations_select_org_members on public.organizations
for select to authenticated
using (id = (select private.current_org_id()));

create policy profiles_select_org_members on public.profiles
for select to authenticated
using (org_id = (select private.current_org_id()));

create policy profiles_update_self_or_admin on public.profiles
for update to authenticated
using (
    org_id = (select private.current_org_id())
    and (id = (select auth.uid()) or (select private.is_admin()))
)
with check (
    org_id = (select private.current_org_id())
    and (id = (select auth.uid()) or (select private.is_admin()))
);

create policy timetables_select_org_members on public.timetables
for select to authenticated
using (org_id = (select private.current_org_id()));
create policy timetables_insert_admin on public.timetables
for insert to authenticated
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetables_update_admin on public.timetables
for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy timetables_delete_admin on public.timetables
for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy periods_select_org_members on public.periods
for select to authenticated
using (exists (
    select 1 from public.timetables as t
    where t.id = periods.timetable_id
      and t.org_id = (select private.current_org_id())
));
create policy periods_insert_admin on public.periods
for insert to authenticated
with check ((select private.is_admin()) and exists (
    select 1 from public.timetables as t
    where t.id = periods.timetable_id
      and t.org_id = (select private.current_org_id())
));
create policy periods_update_admin on public.periods
for update to authenticated
using ((select private.is_admin()) and exists (
    select 1 from public.timetables as t
    where t.id = periods.timetable_id
      and t.org_id = (select private.current_org_id())
))
with check ((select private.is_admin()) and exists (
    select 1 from public.timetables as t
    where t.id = periods.timetable_id
      and t.org_id = (select private.current_org_id())
));
create policy periods_delete_admin on public.periods
for delete to authenticated
using ((select private.is_admin()) and exists (
    select 1 from public.timetables as t
    where t.id = periods.timetable_id
      and t.org_id = (select private.current_org_id())
));

create policy week_schedule_select_org_members on public.week_schedule
for select to authenticated
using (org_id = (select private.current_org_id()));
create policy week_schedule_insert_admin on public.week_schedule
for insert to authenticated
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy week_schedule_update_admin on public.week_schedule
for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy week_schedule_delete_admin on public.week_schedule
for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy date_overrides_select_org_members on public.date_overrides
for select to authenticated
using (org_id = (select private.current_org_id()));
create policy date_overrides_insert_admin on public.date_overrides
for insert to authenticated
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy date_overrides_update_admin on public.date_overrides
for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy date_overrides_delete_admin on public.date_overrides
for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy announcements_select_org_members on public.announcements
for select to authenticated
using (org_id = (select private.current_org_id()));
create policy announcements_insert_admin on public.announcements
for insert to authenticated
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy announcements_update_admin on public.announcements
for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy announcements_delete_admin on public.announcements
for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy audit_log_select_admin on public.audit_log
for select to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));
