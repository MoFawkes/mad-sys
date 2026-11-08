-- Weekly reminders are staff-only; student devices do not subscribe to this table.
create table public.recurring_reminders (
    id uuid primary key default gen_random_uuid(),
    org_id uuid not null references public.organizations(id) on delete cascade,
    title text not null check (char_length(btrim(title)) between 1 and 200),
    body text not null check (char_length(btrim(body)) between 1 and 2000),
    weekdays integer not null check (weekdays between 1 and 127),
    timing text not null check (timing in ('lesson_end', 'fixed_time')),
    fixed_time time null,
    is_enabled boolean not null default true,
    constraint recurring_reminders_fixed_time_check check (
        (timing = 'lesson_end' and fixed_time is null) or
        (timing = 'fixed_time' and fixed_time is not null and fixed_time < time '24:00'
         and extract(second from fixed_time) = 0)
    )
);
create index recurring_reminders_org_id_idx on public.recurring_reminders(org_id);
alter table public.recurring_reminders enable row level security;
revoke all on public.recurring_reminders from public, anon;
grant select, insert, update, delete on public.recurring_reminders to authenticated;

create policy recurring_reminders_select_staff on public.recurring_reminders
for select to authenticated using (
    org_id = (select private.current_org_id()) and exists (
        select 1 from public.profiles p where p.id = (select auth.uid())
        and p.is_active and p.role in ('admin', 'teacher')
    )
);
create policy recurring_reminders_insert_admin on public.recurring_reminders
for insert to authenticated with check (
    (select private.is_admin()) and org_id = (select private.current_org_id())
);
create policy recurring_reminders_update_admin on public.recurring_reminders
for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy recurring_reminders_delete_admin on public.recurring_reminders
for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create trigger recurring_reminders_audit after insert or update or delete on public.recurring_reminders
for each row execute function private.audit_row_change();
alter publication supabase_realtime add table public.recurring_reminders;
