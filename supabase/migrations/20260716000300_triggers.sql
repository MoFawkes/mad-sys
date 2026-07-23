create function private.audit_row_change()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    row_before jsonb;
    row_after jsonb;
    source_row jsonb;
    resolved_org_id uuid;
    resolved_actor_id uuid := auth.uid();
begin
    row_before := case when tg_op in ('UPDATE', 'DELETE') then to_jsonb(old) end;
    row_after := case when tg_op in ('INSERT', 'UPDATE') then to_jsonb(new) end;
    source_row := coalesce(row_after, row_before);

    if source_row ? 'org_id' then
        resolved_org_id := nullif(source_row ->> 'org_id', '')::uuid;
    elsif tg_table_name = 'periods' then
        select t.org_id into resolved_org_id
        from public.timetables as t
        where t.id = (source_row ->> 'timetable_id')::uuid;
    end if;

    if resolved_org_id is null and resolved_actor_id is not null then
        select p.org_id into resolved_org_id
        from public.profiles as p
        where p.id = resolved_actor_id;
    end if;

    insert into public.audit_log (
        org_id, actor_id, action, entity_type, entity_id, before, after
    ) values (
        resolved_org_id,
        resolved_actor_id,
        lower(tg_op),
        tg_table_name,
        (source_row ->> 'id')::uuid,
        row_before,
        row_after
    );

    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;

revoke all on function private.audit_row_change() from public, anon, authenticated;

create trigger timetables_audit after insert or update or delete on public.timetables
for each row execute function private.audit_row_change();
create trigger periods_audit after insert or update or delete on public.periods
for each row execute function private.audit_row_change();
create trigger week_schedule_audit after insert or update or delete on public.week_schedule
for each row execute function private.audit_row_change();
create trigger date_overrides_audit after insert or update or delete on public.date_overrides
for each row execute function private.audit_row_change();
create trigger announcements_audit after insert or update or delete on public.announcements
for each row execute function private.audit_row_change();
create trigger profiles_audit after insert or update or delete on public.profiles
for each row execute function private.audit_row_change();

create function private.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    target_org_id uuid;
    target_display_name text;
begin
    select o.id into target_org_id
    from public.organizations as o
    order by o.created_at, o.id
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

revoke all on function private.handle_new_user() from public, anon, authenticated;

create trigger on_auth_user_created
after insert on auth.users
for each row execute function private.handle_new_user();

create function private.guard_profile_columns()
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
        raise exception 'Staff may update only their own display_name'
            using errcode = '42501';
    end if;

    return new;
end;
$$;

revoke all on function private.guard_profile_columns() from public, anon, authenticated;

create trigger profiles_guard_columns
before update on public.profiles
for each row execute function private.guard_profile_columns();

create function private.guard_last_admin()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    removes_active_admin boolean;
    remaining_admins integer;
begin
    if tg_op = 'DELETE' then
        removes_active_admin := old.role = 'admin' and old.is_active;
    else
        removes_active_admin := old.role = 'admin' and old.is_active and (
            new.role <> 'admin' or not new.is_active or new.org_id <> old.org_id
        );
    end if;

    if not removes_active_admin then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

    perform pg_advisory_xact_lock(hashtextextended(old.org_id::text, 0));

    select count(*) into remaining_admins
    from public.profiles as p
    where p.org_id = old.org_id
      and p.role = 'admin'
      and p.is_active
      and p.id <> old.id;

    if remaining_admins = 0 then
        raise exception 'Cannot remove or deactivate the last active admin'
            using errcode = '23514';
    end if;

    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;

revoke all on function private.guard_last_admin() from public, anon, authenticated;

create trigger profiles_guard_last_admin
before update or delete on public.profiles
for each row execute function private.guard_last_admin();

alter publication supabase_realtime add table
    public.timetables,
    public.periods,
    public.week_schedule,
    public.date_overrides,
    public.announcements,
    public.profiles;
