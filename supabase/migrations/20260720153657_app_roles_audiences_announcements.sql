-- Roles are string-backed so reserving graduate now avoids a later data migration.
alter table public.profiles drop constraint profiles_role_check;
update public.profiles set role = 'teacher' where role = 'staff';
alter table public.profiles alter column role set default 'teacher';
alter table public.profiles add constraint profiles_role_check
  check (role in ('teacher', 'admin', 'graduate'));

-- Production already ran the frozen 20260716000300 migration, so the renamed
-- role wording must arrive as a replace here, not as an edit to that file.
create or replace function private.guard_profile_columns()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if auth.uid() is null or (select private.is_admin()) then
        return new;
    end if;

    if old.id <> auth.uid()
       or new.id is distinct from old.id
       or new.org_id is distinct from old.org_id
       or new.role is distinct from old.role
       or new.is_active is distinct from old.is_active
       or new.created_at is distinct from old.created_at then
        raise exception 'Teachers may update only their own display_name'
            using errcode = '42501';
    end if;

    return new;
end;
$$;

create table public.classes (
  id uuid primary key default gen_random_uuid(),
  org_id uuid not null references public.organizations(id) on delete cascade,
  name text not null,
  sort_order integer not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint classes_org_name_key unique (org_id, name),
  constraint classes_org_sort_order_key unique (org_id, sort_order)
);

create table public.period_classes (
  period_id uuid not null references public.periods(id) on delete cascade,
  class_id uuid not null references public.classes(id) on delete cascade,
  primary key (period_id, class_id)
);

create index classes_org_sort_order_idx on public.classes (org_id, sort_order);
create index period_classes_class_id_idx on public.period_classes (class_id);

create trigger classes_set_updated_at before update on public.classes
for each row execute function private.set_updated_at();
create trigger classes_audit after insert or update or delete on public.classes
for each row execute function private.audit_row_change();

grant select, insert, update, delete on public.classes to authenticated;
grant select, insert, update, delete on public.period_classes to authenticated;
alter table public.classes enable row level security;
alter table public.period_classes enable row level security;

create policy classes_select_org_members on public.classes for select to authenticated
using (org_id = (select private.current_org_id()));
create policy classes_insert_admin on public.classes for insert to authenticated
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy classes_update_admin on public.classes for update to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()))
with check ((select private.is_admin()) and org_id = (select private.current_org_id()));
create policy classes_delete_admin on public.classes for delete to authenticated
using ((select private.is_admin()) and org_id = (select private.current_org_id()));

create policy period_classes_select_org_members on public.period_classes for select to authenticated
using (exists (
  select 1 from public.classes c
  where c.id = period_classes.class_id and c.org_id = (select private.current_org_id())
));
create policy period_classes_insert_admin on public.period_classes for insert to authenticated
with check ((select private.is_admin()) and exists (
  select 1
  from public.classes c
  join public.periods p on p.id = period_classes.period_id
  join public.timetables t on t.id = p.timetable_id
  where c.id = period_classes.class_id
    and c.org_id = (select private.current_org_id())
    and t.org_id = c.org_id
));
create policy period_classes_delete_admin on public.period_classes for delete to authenticated
using ((select private.is_admin()) and exists (
  select 1 from public.classes c
  where c.id = period_classes.class_id and c.org_id = (select private.current_org_id())
));

alter table public.announcements
  add column audience_type text not null default 'everyone'
    constraint announcements_audience_type_check check (audience_type in ('everyone','teachers','graduates','am','pm','specific_class')),
  add column audience_class_id uuid references public.classes(id) on delete restrict,
  add column update_type text not null default 'general'
    constraint announcements_update_type_check check (update_type in ('general','class_starts','naseehah','monthly_programme','yearly_programme')),
  add column publish_at timestamptz,
  add column e_masjid_link text,
  add column status text not null default 'published'
    constraint announcements_status_check check (status in ('draft','scheduled','published')),
  add column deleted_at timestamptz,
  add constraint announcements_audience_class_check check (
    (audience_type = 'specific_class' and audience_class_id is not null)
    or (audience_type <> 'specific_class' and audience_class_id is null)
  ),
  add constraint announcements_e_masjid_link_check check (
    e_masjid_link is null or e_masjid_link ~ '^https://'
  );

create index announcements_active_publish_idx
  on public.announcements (org_id, publish_at)
  where deleted_at is null and status <> 'draft';
create index announcements_audience_class_id_idx
  on public.announcements (audience_class_id)
  where audience_class_id is not null;

alter publication supabase_realtime add table public.classes, public.period_classes;
