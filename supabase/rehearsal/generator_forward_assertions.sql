-- Assertions for 20260827223000 + generator_state.sql + the v0.15 forward
-- migrations. Every failure aborts the rehearsal via ON_ERROR_STOP.

do $$
declare
    duplicate_name text;
begin
    if (select count(*) from public.organization_anchors
        where org_id = '00000000-0000-0000-0000-000000009001') <> 4 then
        raise exception 'The four hosted-equivalent anchors did not survive';
    end if;
    if not exists (
        select 1 from public.organization_anchors
        where id = '00000000-0000-0000-0000-000000009103'
          and org_id = '00000000-0000-0000-0000-000000009001'
          and key = 'maghrib' and name = 'Maghrib' and sort_order = 2
    ) then
        raise exception 'The existing Maghrib anchor identity/value changed';
    end if;

    if (select count(*) from public.anchor_standing_times
        where org_id = '00000000-0000-0000-0000-000000009001') <> 3 then
        raise exception 'Hosted-equivalent standing times did not survive';
    end if;
    if not exists (
        select 1 from public.anchor_standing_times
        where id = '00000000-0000-0000-0000-000000009203'
          and anchor_id = '00000000-0000-0000-0000-000000009101'
          and weekday = 4 and start_time = time '13:15'
          and duration_minutes is null and effective_from = date '2026-09-01'
    ) then
        raise exception 'Friday Jumu''ah standing time changed or became zero';
    end if;

    if (select count(*) from public.anchor_date_overrides
        where org_id = '00000000-0000-0000-0000-000000009001') <> 2
       or not exists (
        select 1 from public.anchor_date_overrides
        where id = '00000000-0000-0000-0000-000000009301'
          and anchor_id = '00000000-0000-0000-0000-000000009103'
          and date = date '2026-09-26' and start_time = time '18:57'
          and duration_minutes = 15 and not is_cancelled
    ) then
        raise exception 'Hosted-equivalent Maghrib overrides did not survive';
    end if;

    if (select count(*) from public.generator_maintenance_runs
        where org_id = '00000000-0000-0000-0000-000000009001') <> 2
       or not exists (
        select 1 from public.generator_maintenance_runs
        where id = '00000000-0000-0000-0000-000000009402'
          and duration_ms = 84
          and error = 'historic rehearsal error'
    ) then
        raise exception 'Generator maintenance history did not survive';
    end if;

    if to_regclass('public.timetable_generator_blocks') is not null
       or to_regclass('public.timetable_generator_anchors') is not null then
        raise exception 'Obsolete generator child tables still exist';
    end if;

    if exists (
        select 1 from pg_policies
        where schemaname = 'public'
          and tablename in (
              'organization_anchors', 'anchor_standing_times',
              'anchor_date_overrides', 'timetable_generators')
          and (cmd <> 'SELECT' or policyname ~ '_(insert|update|delete)_admin$')
    ) then
        raise exception 'A legacy generator write policy survived';
    end if;
    if exists (
        select 1 from information_schema.role_table_grants
        where table_schema = 'public'
          and table_name in (
              'organization_anchors', 'anchor_standing_times',
              'anchor_date_overrides', 'timetable_generators')
          and grantee = 'authenticated' and privilege_type <> 'SELECT'
    ) then
        raise exception 'authenticated retained direct generator write access';
    end if;
    if exists (
        select 1 from information_schema.role_table_grants
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
              'run_generator_maintenance', 'generator_maintenance',
              'expand_generated_timetable', 'expand_timetable_shape',
              'apply_generator_anchors', 'generated_periods_json',
              'generator_guid_sort_key', 'generator_add_minutes',
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
        'public.admin_preview_generated_timetable(uuid,jsonb,jsonb,uuid[])') is not null
       or to_regprocedure('public.admin_save_generated_timetable(uuid,jsonb)') is null
       or to_regprocedure(
        'public.admin_preview_generated_timetable(uuid,jsonb,date)') is null then
        raise exception 'Generator entry-point signatures are not exactly v0.15';
    end if;
end $$;
