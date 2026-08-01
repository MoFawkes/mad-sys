-- Keep the join-code admin RPCs fail-closed if their backing row is missing,
-- and classify mass device revocation separately from code rotation in audit.

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

  if not found then
    raise exception 'Student join code is unavailable'
      using errcode = '42501';
  end if;

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
    'student_devices',
    caller_org_id,
    null,
    jsonb_build_object('revoked_device_count', affected_count)
  );

  return affected_count;
end;
$$;

revoke all on function public.rotate_student_join_code() from public, anon;
revoke all on function public.revoke_student_devices() from public, anon;
grant execute on function public.rotate_student_join_code() to authenticated;
grant execute on function public.revoke_student_devices() to authenticated;
