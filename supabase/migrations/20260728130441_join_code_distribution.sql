-- Keep the student join credential out of organizations: the shipped desktop
-- client pulls that table with select=*, so column-level grants would break it.
create table public.organization_join_codes (
  org_id uuid primary key references public.organizations(id) on delete cascade,
  code text not null unique
    constraint organization_join_codes_code_format_check
    check (code ~ '^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{16}$'),
  rotated_at timestamptz not null default now(),
  rotated_by uuid references auth.users(id)
);

alter table public.organization_join_codes enable row level security;
revoke all on public.organization_join_codes from public, anon, authenticated;

insert into public.organization_join_codes (org_id, code)
select organization.id, organization.student_join_code
from public.organizations as organization;

alter table public.organizations drop column student_join_code;

create or replace function private.assign_join_code()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  generated_code text;
begin
  loop
    generated_code := private.generate_student_join_code();
    exit when not exists (
      select 1
      from public.organization_join_codes as join_code
      where join_code.code = generated_code
    );
  end loop;

  insert into public.organization_join_codes (org_id, code)
  values (new.id, generated_code);
  return new;
end;
$$;

revoke all on function private.assign_join_code() from public, anon, authenticated;

create trigger organizations_assign_join_code
after insert on public.organizations
for each row execute function private.assign_join_code();

create or replace function public.enroll_student_device(join_code text)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
  caller_id uuid := (select auth.uid());
  normalized_code text := upper(
    regexp_replace(coalesce(join_code, ''), '[^0-9A-Za-z]', '', 'g')
  );
  resolved_org_id uuid;
begin
  if caller_id is null
     or coalesce((select auth.jwt()->>'is_anonymous'), 'false') <> 'true' then
    raise exception 'Student device enrolment requires an anonymous authenticated user'
      using errcode = '42501';
  end if;

  select stored_code.org_id
  into resolved_org_id
  from public.organization_join_codes as stored_code
  where stored_code.code = normalized_code;

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

create or replace function public.admin_student_join_code()
returns text
language plpgsql
stable
security definer
set search_path = ''
as $$
declare
  caller_org_id uuid;
  result text;
begin
  if not coalesce((select private.is_admin()), false) then
    raise exception 'Administrator access is required'
      using errcode = '42501';
  end if;

  caller_org_id := (select private.current_org_id());
  select join_code.code
  into result
  from public.organization_join_codes as join_code
  where join_code.org_id = caller_org_id;

  if result is null then
    raise exception 'Student join code is unavailable'
      using errcode = '42501';
  end if;
  return result;
end;
$$;

create or replace function public.rotate_student_join_code()
returns text
language plpgsql
security definer
set search_path = ''
as $$
declare
  caller_id uuid := (select auth.uid());
  caller_org_id uuid;
  generated_code text;
  rotation_time timestamptz := now();
begin
  if not coalesce((select private.is_admin()), false) then
    raise exception 'Administrator access is required'
      using errcode = '42501';
  end if;
  caller_org_id := (select private.current_org_id());

  loop
    generated_code := private.generate_student_join_code();
    exit when not exists (
      select 1
      from public.organization_join_codes as join_code
      where join_code.code = generated_code
    );
  end loop;

  update public.organization_join_codes
  set code = generated_code,
      rotated_at = rotation_time,
      rotated_by = caller_id
  where org_id = caller_org_id;

  insert into public.audit_log (
    org_id, actor_id, action, entity_type, entity_id, before, after
  ) values (
    caller_org_id,
    caller_id,
    'update',
    'organization_join_code',
    caller_org_id,
    null,
    jsonb_build_object('rotated_at', rotation_time)
  );

  return generated_code;
end;
$$;

create or replace function public.revoke_student_devices()
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
  caller_id uuid := (select auth.uid());
  caller_org_id uuid;
  affected_count integer;
begin
  if not coalesce((select private.is_admin()), false) then
    raise exception 'Administrator access is required'
      using errcode = '42501';
  end if;
  caller_org_id := (select private.current_org_id());

  delete from public.student_devices
  where org_id = caller_org_id;
  get diagnostics affected_count = row_count;

  insert into public.audit_log (
    org_id, actor_id, action, entity_type, entity_id, before, after
  ) values (
    caller_org_id,
    caller_id,
    'update',
    'organization_join_code',
    caller_org_id,
    null,
    jsonb_build_object('revoked_device_count', affected_count)
  );

  return affected_count;
end;
$$;

revoke all on function public.admin_student_join_code() from public, anon;
revoke all on function public.rotate_student_join_code() from public, anon;
revoke all on function public.revoke_student_devices() from public, anon;
grant execute on function public.admin_student_join_code() to authenticated;
grant execute on function public.rotate_student_join_code() to authenticated;
grant execute on function public.revoke_student_devices() to authenticated;
