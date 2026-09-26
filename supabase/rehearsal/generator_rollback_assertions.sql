-- Assertions after v0_15_generator_down.sql. This is a logical rollback to the
-- pre-generator schema; migration history intentionally remains immutable.

do $$
declare
    versions text;
begin
    if to_regclass('public.organization_anchors') is not null
       or to_regclass('public.anchor_standing_times') is not null
       or to_regclass('public.anchor_date_overrides') is not null
       or to_regclass('public.timetable_generators') is not null
       or to_regclass('public.timetable_generator_blocks') is not null
       or to_regclass('public.timetable_generator_anchors') is not null
       or to_regclass('public.generator_maintenance_runs') is not null then
        raise exception 'Generator tables remain after logical rollback';
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'timetables'
          and column_name = 'is_generated'
    ) then
        raise exception 'timetables.is_generated remains after logical rollback';
    end if;

    if to_regprocedure('public.admin_preview_generated_timetable(uuid,jsonb,date)') is not null
       or to_regprocedure('public.admin_save_generated_timetable(uuid,jsonb)') is not null
       or to_regprocedure('public.admin_disable_generated_timetable(uuid)') is not null
       or to_regprocedure('public.admin_bulk_upsert_anchor_date_overrides(uuid,jsonb)') is not null
       or to_regprocedure('public.admin_save_prayer_fixed_times(jsonb,date)') is not null
       or to_regprocedure('public.admin_regenerate_generated_timetables()') is not null
       or to_regprocedure('public.run_generator_maintenance(uuid)') is not null then
        raise exception 'Generator entry points remain after logical rollback';
    end if;

    if exists (
        select 1 from pg_type type
        join pg_namespace namespace on namespace.oid = type.typnamespace
        where namespace.nspname = 'private'
          and type.typname in (
              'generated_period', 'resolved_generator_anchor',
              'generator_expansion_pass')
    ) then
        raise exception 'Generator composite types remain after logical rollback';
    end if;

    if exists (
        select 1 from pg_trigger
        where not tgisinternal
          and tgname in ('organizations_seed_anchors', 'periods_guard_generated_write')
    ) then
        raise exception 'Generator triggers remain after logical rollback';
    end if;

    if to_regprocedure('public.admin_save_timetable(jsonb,jsonb)') is null then
        raise exception 'Released manual timetable endpoint was not restored';
    end if;

    select string_agg(version, ',' order by version) into versions
    from supabase_migrations.schema_migrations;
    if versions <> '20260716000100,20260716000200,20260716000300,20260720153657,20260727225644,20260728130441,20260728134650,20260803120000,20260807110000,20260807120000,20260826214012,20260826225951,20260827204758,20260827223000,20260925173216,20260925173217' then
        raise exception 'Logical rollback rewrote migration history: %', versions;
    end if;
end $$;

-- The restored audit helper must still support ordinary rows after generator
-- tables with natural keys have gone away.
insert into public.timetables (id, org_id, name, is_archived) values
    ('00000000-0000-0000-0000-000000009601',
     '00000000-0000-0000-0000-000000009001',
     'Manual after rollback', false);

insert into public.periods (
    id, timetable_id, name, start_time, end_time, sort_order, is_lesson
) values (
    '00000000-0000-0000-0000-000000009602',
    '00000000-0000-0000-0000-000000009601',
    'Lesson 1', '09:00', '09:30', 1, true
);

do $$
begin
    if not exists (
        select 1 from public.audit_log
        where entity_type = 'periods'
          and entity_id = '00000000-0000-0000-0000-000000009602'
          and org_id = '00000000-0000-0000-0000-000000009001'
          and action = 'insert'
    ) then
        raise exception 'Released period audit behaviour was not restored';
    end if;
end $$;
