-- Post-migration assertions for the incremental-migration rehearsal.
-- Runs after `supabase migration up` has applied every migration released
-- since v0.9.6 on top of the production-like baseline loaded by
-- production_state.sql.
-- Every failure raises, which fails the psql run via ON_ERROR_STOP.

-- Migration history took the incremental path: all sixteen versions recorded,
-- including the four generator migrations already applied to the hosted estate.
do $$
declare versions text;
begin
    select string_agg(version, ',' order by version) into versions
    from supabase_migrations.schema_migrations;
    if versions <> '20260716000100,20260716000200,20260716000300,20260720153657,20260727225644,20260728130441,20260728134650,20260803120000,20260807110000,20260807120000,20260826214012,20260826225951,20260827204758,20260827223000,20260925173216,20260925173217' then
        raise exception 'Unexpected migration history: %', versions;
    end if;
end $$;

-- v0.15 automation is installed without converting legacy timetable rows.
do $$
begin
    if to_regprocedure('public.admin_save_generated_timetable(uuid,jsonb)') is null
       or to_regprocedure('public.admin_disable_generated_timetable(uuid)') is null
       or to_regprocedure('public.admin_bulk_upsert_anchor_date_overrides(uuid,jsonb)') is null
       or to_regprocedure('public.admin_save_prayer_fixed_times(jsonb,date)') is null
       or to_regprocedure('public.admin_preview_generated_timetable(uuid,jsonb,date)') is null then
        raise exception 'Generator admin write RPCs are not installed';
    end if;
    if to_regclass('public.timetable_generator_blocks') is not null
       or to_regclass('public.timetable_generator_anchors') is not null then
        raise exception 'Discarded generator authoring tables were installed';
    end if;
    if (select count(*) from public.organization_anchors) <> 8 then
        raise exception 'Generator migration did not backfill four anchors for both organizations';
    end if;
    if (select count(*) from public.periods where timetable_id = '00000000-0000-0000-0000-000000000100') <> 3 then
        raise exception 'Generator migrations changed the released legacy timetable';
    end if;
    if exists (select 1 from public.timetables where is_generated)
       or exists (select 1 from public.timetable_generators) then
        raise exception 'Generator migration converted a timetable without teacher opt-in';
    end if;
    if to_regprocedure('public.run_generator_maintenance(uuid)') is null then
        raise exception 'run_generator_maintenance(uuid) is missing';
    end if;
    if to_regprocedure('public.admin_regenerate_generated_timetables()') is null then
        raise exception 'admin_regenerate_generated_timetables() is missing';
    end if;
    if to_regprocedure('private.generator_maintenance(uuid)') is null then
        raise exception 'private.generator_maintenance(uuid) is missing';
    end if;
    if has_function_privilege('authenticated', 'public.run_generator_maintenance(uuid)', 'execute') then
        raise exception 'authenticated can execute the service-only maintenance RPC';
    end if;
    if not has_function_privilege('service_role', 'public.run_generator_maintenance(uuid)', 'execute') then
        raise exception 'service_role cannot execute the scheduled maintenance RPC';
    end if;
    if has_function_privilege('service_role', 'public.admin_regenerate_generated_timetables()', 'execute') then
        raise exception 'service_role can execute the admin-only maintenance RPC';
    end if;
end $$;

-- Forward transition removed every direct-write policy/grant and every old
-- overload. RPC validation is the only authenticated write path.
do $$
declare
    duplicate_name text;
begin
    if exists (
        select 1
        from pg_policies
        where schemaname = 'public'
          and tablename in (
              'organization_anchors', 'anchor_standing_times',
              'anchor_date_overrides', 'timetable_generators')
          and (cmd <> 'SELECT' or policyname ~ '_(insert|update|delete)_admin$')
    ) then
        raise exception 'A legacy generator write policy survived the forward transition';
    end if;

    if exists (
        select 1
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'organization_anchors', 'anchor_standing_times',
              'anchor_date_overrides', 'timetable_generators')
          and grantee = 'authenticated'
          and privilege_type <> 'SELECT'
    ) then
        raise exception 'authenticated retained a direct generator table write grant';
    end if;

    if exists (
        select 1
        from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'organization_anchors', 'anchor_standing_times',
              'anchor_date_overrides', 'timetable_generators',
              'generator_maintenance_runs')
          and grantee = 'anon'
    ) then
        raise exception 'anon retained a generator table privilege';
    end if;

    select function_name into duplicate_name
    from (
        select procedure.proname as function_name, count(*) as overloads
        from pg_proc procedure
        join pg_namespace namespace on namespace.oid = procedure.pronamespace
        where namespace.nspname in ('public', 'private')
          and procedure.proname in (
              'admin_preview_generated_timetable',
              'admin_save_generated_timetable',
              'admin_disable_generated_timetable',
              'admin_bulk_upsert_anchor_date_overrides',
              'admin_save_prayer_fixed_times',
              'admin_regenerate_generated_timetables',
              'run_generator_maintenance',
              'generator_maintenance',
              'expand_generated_timetable',
              'expand_timetable_shape',
              'apply_generator_anchors',
              'generated_periods_json',
              'generator_guid_sort_key',
              'generator_add_minutes',
              'generator_stable_id')
        group by procedure.proname
        having count(*) > 1
    ) duplicates
    limit 1;
    if duplicate_name is not null then
        raise exception 'Generator function overload survived for %', duplicate_name;
    end if;

    if to_regprocedure(
        'public.admin_save_generated_timetable(uuid,jsonb,jsonb,uuid[],jsonb)') is not null
       or to_regprocedure(
        'public.admin_preview_generated_timetable(uuid,jsonb,jsonb,uuid[])') is not null then
        raise exception 'An old generator entry-point signature survived';
    end if;
end $$;

-- v0.13.0 adds audience-aware rows without changing any existing default row.
-- The compatibility and audience RPC signatures must coexist after migration.
do $$
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'week_schedule'
          and column_name = 'audience_class_id' and is_nullable = 'YES'
    ) then
        raise exception 'week_schedule.audience_class_id is missing or not nullable';
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = 'public.week_schedule'::regclass
          and conname = 'week_schedule_org_weekday_audience_key'
    ) then
        raise exception 'Audience-aware week_schedule uniqueness constraint is missing';
    end if;
    if exists (select 1 from public.week_schedule where audience_class_id is not null) then
        raise exception 'Migration unexpectedly created class-specific week_schedule rows';
    end if;
    if to_regprocedure('public.admin_save_week_schedule(smallint,uuid,uuid)') is null then
        raise exception 'admin_save_week_schedule(smallint,uuid,uuid) is missing';
    end if;
    if to_regprocedure('public.admin_delete_week_schedule(smallint,uuid)') is null then
        raise exception 'admin_delete_week_schedule(smallint,uuid) is missing';
    end if;
end $$;

-- v0.11.1 backfilled every existing organisation and exposed the atomic
-- timetable RPC with constraints that can be deferred inside that RPC.
do $$
declare invalid_orgs integer;
begin
    select count(*) into invalid_orgs
    from (
        select organization.id
        from public.organizations organization
        left join public.week_schedule schedule on schedule.org_id = organization.id
        group by organization.id
        having count(schedule.id) <> 7
    ) invalid;
    if invalid_orgs <> 0 then
        raise exception '% organizations do not have exactly seven week_schedule rows', invalid_orgs;
    end if;
    if to_regprocedure('public.admin_save_timetable(jsonb,jsonb)') is null then
        raise exception 'admin_save_timetable(jsonb,jsonb) is missing';
    end if;
    if (select count(*) from pg_constraint
        where conrelid = 'public.periods'::regclass
          and conname in ('periods_timetable_name_key', 'periods_timetable_sort_order_key')
          and condeferrable) <> 2 then
        raise exception 'period unique constraints are not both deferrable';
    end if;
end $$;

-- v0.11.3 exposed the two-argument week-schedule compatibility RPC. Clients
-- released before v0.13.0 save default rows through it, so it must survive
-- every later migration; the audience migration drops the conflict arbiter its
-- predecessor relied on. Behaviour is covered by GatewaySmokeTests, which can
-- authenticate; this only proves the migration path keeps the signature.
do $$
begin
    if to_regprocedure('public.admin_save_week_schedule(smallint,uuid)') is null then
        raise exception 'admin_save_week_schedule(smallint,uuid) is missing';
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
