-- Hosted-equivalent fixture at migration 20260827223000.
-- It deliberately exercises every shared generator table that can contain
-- production data while leaving the guarded old authoring tables empty.

insert into public.organizations (id, name, timezone) values
    ('00000000-0000-0000-0000-000000009001',
     'Generator forward rehearsal', 'Europe/London');

-- organizations_seed_anchors created these rows. Give them stable ids so the
-- post-migration rehearsal proves ALTER preserved identity as well as values.
update public.organization_anchors
set id = case key
    when 'zuhr' then '00000000-0000-0000-0000-000000009101'::uuid
    when 'asr' then '00000000-0000-0000-0000-000000009102'::uuid
    when 'maghrib' then '00000000-0000-0000-0000-000000009103'::uuid
    when 'isha' then '00000000-0000-0000-0000-000000009104'::uuid
end
where org_id = '00000000-0000-0000-0000-000000009001';

insert into public.anchor_standing_times (
    id, org_id, anchor_id, weekday, start_time, duration_minutes,
    effective_from, created_at, updated_at
) values
    ('00000000-0000-0000-0000-000000009201',
     '00000000-0000-0000-0000-000000009001',
     '00000000-0000-0000-0000-000000009102', null,
     '16:45', 20, date '2026-09-01',
     timestamptz '2026-09-01 09:00:00+00', timestamptz '2026-09-01 09:00:00+00'),
    ('00000000-0000-0000-0000-000000009202',
     '00000000-0000-0000-0000-000000009001',
     '00000000-0000-0000-0000-000000009104', null,
     '20:15', 20, date '2026-09-01',
     timestamptz '2026-09-01 09:01:00+00', timestamptz '2026-09-01 09:01:00+00'),
    ('00000000-0000-0000-0000-000000009203',
     '00000000-0000-0000-0000-000000009001',
     '00000000-0000-0000-0000-000000009101', 4,
     '13:15', null, date '2026-09-01',
     timestamptz '2026-09-01 09:02:00+00', timestamptz '2026-09-01 09:02:00+00');

insert into public.anchor_date_overrides (
    id, org_id, anchor_id, date, start_time, duration_minutes,
    is_cancelled, created_at, updated_at
) values
    ('00000000-0000-0000-0000-000000009301',
     '00000000-0000-0000-0000-000000009001',
     '00000000-0000-0000-0000-000000009103', date '2026-09-26',
     '18:57', 15, false,
     timestamptz '2026-09-01 10:00:00+00', timestamptz '2026-09-01 10:00:00+00'),
    ('00000000-0000-0000-0000-000000009302',
     '00000000-0000-0000-0000-000000009001',
     '00000000-0000-0000-0000-000000009103', date '2026-09-27',
     '18:55', 15, false,
     timestamptz '2026-09-01 10:01:00+00', timestamptz '2026-09-01 10:01:00+00');

insert into public.generator_maintenance_runs (
    id, org_id, started_at, duration_ms, regenerated_date,
    timetables_written, error, created_at
) values
    ('00000000-0000-0000-0000-000000009401',
     '00000000-0000-0000-0000-000000009001',
     timestamptz '2026-09-25 02:00:00+00', 42, date '2026-09-25',
     0, null, timestamptz '2026-09-25 02:00:00+00'),
    ('00000000-0000-0000-0000-000000009402',
     '00000000-0000-0000-0000-000000009001',
     timestamptz '2026-09-26 02:00:00+00', 84, date '2026-09-26',
     0, 'historic rehearsal error', timestamptz '2026-09-26 02:00:00+00');
