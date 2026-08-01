-- GoTrue requires global signup to be enabled before it will create anonymous
-- users. This hook preserves the existing invite-only email posture. It is
-- invoked for public and anonymous signup; Admin API creation bypasses it.
create or replace function private.before_user_created(event jsonb)
returns jsonb
language plpgsql
set search_path = ''
as $$
begin
  if jsonb_typeof(event -> 'user' -> 'is_anonymous') = 'boolean'
     and (event -> 'user' ->> 'is_anonymous')::boolean is true then
    return '{}'::jsonb;
  end if;

  return jsonb_build_object(
    'error',
    jsonb_build_object(
      'http_code', 403,
      'message', 'Public signup is disabled'
    )
  );
end;
$$;

grant usage on schema private to supabase_auth_admin;
grant execute on function private.before_user_created(jsonb) to supabase_auth_admin;
revoke execute on function private.before_user_created(jsonb) from public, anon, authenticated;

alter table public.organizations
  add column student_join_code text unique;

create function private.generate_student_join_code()
returns text
language plpgsql
volatile
security invoker
set search_path = ''
as $$
declare
  alphabet constant text := '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
  random_bytes bytea := extensions.gen_random_bytes(16);
  generated_code text := '';
  byte_index integer;
begin
  for byte_index in 0..15 loop
    generated_code := generated_code
      || substr(alphabet, (get_byte(random_bytes, byte_index) % length(alphabet)) + 1, 1);
  end loop;
  return generated_code;
end;
$$;

revoke all on function private.generate_student_join_code() from public, anon, authenticated;

alter table public.organizations
  alter column student_join_code set default private.generate_student_join_code();

-- Anonymous device identities must not be promoted into ordinary org-member profiles.
create or replace function private.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  target_org_id uuid;
  target_display_name text;
begin
  if new.is_anonymous then
    return new;
  end if;

  select organization.id
  into target_org_id
  from public.organizations as organization
  order by organization.created_at, organization.id
  limit 1;

  if target_org_id is null then
    raise exception 'Cannot create profile: no organization has been configured';
  end if;

  target_display_name := coalesce(
    nullif(new.raw_user_meta_data ->> 'display_name', ''),
    nullif(split_part(coalesce(new.email, ''), '@', 1), ''),
    'User'
  );

  insert into public.profiles (id, org_id, display_name)
  values (new.id, target_org_id, target_display_name);

  return new;
end;
$$;

do $$
declare
  organization record;
  generated_code text;
begin
  for organization in select id from public.organizations loop
    loop
      generated_code := private.generate_student_join_code();
      exit when not exists (
        select 1
        from public.organizations
        where student_join_code = generated_code
      );
    end loop;

    update public.organizations
    set student_join_code = generated_code
    where id = organization.id;
  end loop;
end;
$$;

alter table public.organizations
  alter column student_join_code set not null,
  add constraint organizations_student_join_code_format_check
    check (student_join_code ~ '^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{16}$');

create table public.student_devices (
  user_id uuid primary key references auth.users(id) on delete cascade,
  org_id uuid not null references public.organizations(id) on delete cascade,
  created_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now()
);

create index student_devices_org_id_idx on public.student_devices (org_id);

alter table public.student_devices enable row level security;

grant select on public.student_devices to authenticated;

create policy student_devices_select_own on public.student_devices
for select to authenticated
using (user_id = (select auth.uid()));

create or replace function public.enroll_student_device(join_code text)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
  caller_id uuid := (select auth.uid());
  resolved_org_id uuid;
begin
  if caller_id is null
     or coalesce((select auth.jwt()->>'is_anonymous'), 'false') <> 'true' then
    raise exception 'Student device enrolment requires an anonymous authenticated user'
      using errcode = '42501';
  end if;

  select organization.id
  into resolved_org_id
  from public.organizations as organization
  where organization.student_join_code = upper(trim(join_code));

  if resolved_org_id is null then
    raise exception 'Invalid student join code'
      using errcode = '42501';
  end if;

  insert into public.student_devices (user_id, org_id)
  values (caller_id, resolved_org_id)
  on conflict (user_id) do update
  set org_id = excluded.org_id,
      last_seen_at = now();

  return resolved_org_id;
end;
$$;

revoke all on function public.enroll_student_device(text) from public, anon;
grant execute on function public.enroll_student_device(text) to authenticated;

create or replace function private.current_device_org_id()
returns uuid
language sql
stable
security definer
set search_path = ''
as $$
  select device.org_id
  from public.student_devices as device
  where device.user_id = (select auth.uid())
$$;

revoke all on function private.current_device_org_id() from public, anon, authenticated;
grant execute on function private.current_device_org_id() to authenticated;

create policy organizations_select_student_devices on public.organizations
for select to authenticated
using (id = (select private.current_device_org_id()));

create policy timetables_select_student_devices on public.timetables
for select to authenticated
using (org_id = (select private.current_device_org_id()));

create policy periods_select_student_devices on public.periods
for select to authenticated
using (exists (
  select 1
  from public.timetables as timetable
  where timetable.id = periods.timetable_id
    and timetable.org_id = (select private.current_device_org_id())
));

create policy classes_select_student_devices on public.classes
for select to authenticated
using (org_id = (select private.current_device_org_id()));

create policy period_classes_select_student_devices on public.period_classes
for select to authenticated
using (exists (
  select 1
  from public.classes as class_row
  where class_row.id = period_classes.class_id
    and class_row.org_id = (select private.current_device_org_id())
));

create policy week_schedule_select_student_devices on public.week_schedule
for select to authenticated
using (org_id = (select private.current_device_org_id()));

create policy date_overrides_select_student_devices on public.date_overrides
for select to authenticated
using (org_id = (select private.current_device_org_id()));

create policy announcements_select_student_devices on public.announcements
for select to authenticated
using (
  org_id = (select private.current_device_org_id())
  and deleted_at is null
  and status <> 'draft'
  and (publish_at is null or publish_at <= now())
  and audience_type not in ('teachers', 'graduates')
);
