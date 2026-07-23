-- Post-migration assertions for the incremental-migration rehearsal.
-- Runs after `supabase migration up` has applied
-- 20260720153657_app_roles_audiences_announcements.sql on top of the
-- production-like baseline loaded by production_state.sql.
-- Every failure raises, which fails the psql run via ON_ERROR_STOP.

-- Migration history took the incremental path: all four versions recorded.
do $$
declare versions text;
begin
    select string_agg(version, ',' order by version) into versions
    from supabase_migrations.schema_migrations;
    if versions <> '20260716000100,20260716000200,20260716000300,20260720153657' then
        raise exception 'Unexpected migration history: %', versions;
    end if;
end $$;

-- Role rename covered every profile, including the deactivated one.
do $$
declare staff_left integer;
begin
    select count(*) into staff_left from public.profiles where role = 'staff';
    if staff_left <> 0 then
        raise exception '% profiles still have role staff', staff_left;
    end if;
    if (select role from public.profiles where id = '00000000-0000-0000-0000-000000000501') <> 'admin' then
        raise exception 'Admin profile lost its role in the rename';
    end if;
    if (select role from public.profiles where id = '00000000-0000-0000-0000-000000000502') <> 'teacher' then
        raise exception 'Staff profile was not renamed to teacher';
    end if;
    if not exists (
        select 1 from public.profiles
        where id = '00000000-0000-0000-0000-000000000503' and role = 'teacher' and not is_active
    ) then
        raise exception 'Deactivated staff profile was not renamed or lost is_active=false';
    end if;
    if (select column_default from information_schema.columns
        where table_schema = 'public' and table_name = 'profiles' and column_name = 'role')
       <> '''teacher''::text' then
        raise exception 'profiles.role default is not teacher';
    end if;
end $$;

-- The rebuilt role check accepts graduate and rejects the retired staff value.
do $$
begin
    update public.profiles set role = 'graduate' where id = '00000000-0000-0000-0000-000000000502';
    update public.profiles set role = 'teacher' where id = '00000000-0000-0000-0000-000000000502';
    begin
        update public.profiles set role = 'staff' where id = '00000000-0000-0000-0000-000000000502';
        raise exception 'Retired staff role was accepted after the migration';
    exception when check_violation then null;
    end;
end $$;

-- The replaced guard function carries the renamed wording (production gets it
-- via create or replace, not via the frozen 20260716000300 file).
do $$
begin
    if (select prosrc from pg_proc where oid = 'private.guard_profile_columns()'::regprocedure)
       not like '%Teachers may update only their own display_name%' then
        raise exception 'guard_profile_columns still carries the pre-rename wording';
    end if;
end $$;

-- Pre-existing announcements survived with defaults that keep them visible
-- to the app (published, no publish_at gate, not deleted).
do $$
declare total integer; wrong integer;
begin
    select count(*),
           count(*) filter (where not (
               audience_type = 'everyone' and audience_class_id is null
               and update_type = 'general' and status = 'published'
               and publish_at is null and deleted_at is null and e_masjid_link is null))
    into total, wrong
    from public.announcements
    where id in ('00000000-0000-0000-0000-000000000511',
                 '00000000-0000-0000-0000-000000000512',
                 '00000000-0000-0000-0000-000000000513',
                 '00000000-0000-0000-0000-000000000514');
    if total <> 4 then
        raise exception 'Expected 4 pre-existing announcements, found %', total;
    end if;
    if wrong <> 0 then
        raise exception '% pre-existing announcements did not receive visible defaults', wrong;
    end if;
end $$;

-- New announcement constraints hold on migrated rows.
do $$
begin
    begin
        update public.announcements set e_masjid_link = 'http://insecure.example'
        where id = '00000000-0000-0000-0000-000000000511';
        raise exception 'Non-https e-Masjid link was accepted';
    exception when check_violation then null;
    end;
    begin
        update public.announcements set audience_type = 'specific_class'
        where id = '00000000-0000-0000-0000-000000000511';
        raise exception 'specific_class without a class id was accepted';
    exception when check_violation then null;
    end;
    update public.announcements set e_masjid_link = 'https://emasjid.example/event'
    where id = '00000000-0000-0000-0000-000000000511';
    update public.announcements set e_masjid_link = null
    where id = '00000000-0000-0000-0000-000000000511';
end $$;

-- New tables arrived with row security and realtime publication membership.
do $$
declare tables text;
begin
    if not (select relrowsecurity from pg_class where oid = 'public.classes'::regclass) then
        raise exception 'classes has row level security disabled';
    end if;
    if not (select relrowsecurity from pg_class where oid = 'public.period_classes'::regclass) then
        raise exception 'period_classes has row level security disabled';
    end if;
    select string_agg(tablename, ',' order by tablename) into tables
    from pg_publication_tables
    where pubname = 'supabase_realtime' and schemaname = 'public';
    if tables <> 'announcements,classes,date_overrides,period_classes,periods,profiles,timetables,week_schedule' then
        raise exception 'Unexpected realtime publication tables: %', tables;
    end if;
end $$;

-- Audit and updated_at triggers are live on classes; period links cascade.
insert into public.classes (id, org_id, name, sort_order) values
    ('00000000-0000-0000-0000-000000000531', '00000000-0000-0000-0000-000000000001',
     'Rehearsal class', 900);
update public.classes set name = 'Rehearsal class renamed'
where id = '00000000-0000-0000-0000-000000000531';
insert into public.period_classes (period_id, class_id) values
    ('00000000-0000-0000-0000-000000000301', '00000000-0000-0000-0000-000000000531');

do $$
declare audit_rows integer; links integer;
begin
    select count(*) into audit_rows from public.audit_log
    where entity_type = 'classes'
      and entity_id = '00000000-0000-0000-0000-000000000531';
    if audit_rows <> 2 then
        raise exception 'Expected 2 classes audit rows (insert, update), found %', audit_rows;
    end if;
    if (select updated_at < created_at from public.classes
        where id = '00000000-0000-0000-0000-000000000531') then
        raise exception 'classes updated_at trigger did not fire';
    end if;
    delete from public.classes where id = '00000000-0000-0000-0000-000000000531';
    select count(*) into links from public.period_classes
    where class_id = '00000000-0000-0000-0000-000000000531';
    if links <> 0 then
        raise exception 'period_classes did not cascade on class delete';
    end if;
end $$;
