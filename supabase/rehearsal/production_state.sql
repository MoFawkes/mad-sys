-- Production-like data for the incremental-migration rehearsal.
-- Runs against the RELEASED baseline schema (migrations up to 20260716000300
-- plus the baseline-compatible fixture below), i.e. the state the production database is in on v0.9.6:
-- profiles still use role 'staff' and announcements have no audience columns.
-- Emails deliberately avoid the 'aqitest-' prefix that SupabaseFixture deletes.

-- Keep the released-schema fixture here instead of running the current seed.sql:
-- the current seed evolves with the head schema and may reference columns that do
-- not exist at the historical rehearsal baseline.
insert into public.organizations (id, name, timezone) values
    ('00000000-0000-0000-0000-000000000001', 'AQI', 'Europe/London');

insert into public.timetables (id, org_id, name, is_archived) values
    ('00000000-0000-0000-0000-000000000100',
     '00000000-0000-0000-0000-000000000001', 'Normal Day', false);

insert into public.week_schedule (id, org_id, weekday, timetable_id)
select
    ('00000000-0000-0000-0000-' || lpad((200 + weekday)::text, 12, '0'))::uuid,
    '00000000-0000-0000-0000-000000000001'::uuid,
    weekday,
    null
from generate_series(0, 6) as weekday
on conflict (org_id, weekday) do update
set id = excluded.id,
    timetable_id = excluded.timetable_id;

insert into public.periods (
    id, timetable_id, name, start_time, end_time, sort_order, is_lesson
) values
    ('00000000-0000-0000-0000-000000000301', '00000000-0000-0000-0000-000000000100', 'Registration', '08:25', '08:45', 1, true),
    ('00000000-0000-0000-0000-000000000302', '00000000-0000-0000-0000-000000000100', 'Period 1', '08:45', '09:45', 2, true),
    ('00000000-0000-0000-0000-000000000303', '00000000-0000-0000-0000-000000000100', 'Break', '09:45', '10:05', 3, false);

-- Mirrors a production-created organisation that never ran seed.sql and therefore
-- has no week_schedule rows before the v0.11.1 backfill.
insert into public.organizations (id, name) values
    ('00000000-0000-0000-0000-000000000099', 'Rehearsal unseeded organisation');

insert into auth.users (
    instance_id, id, aud, role, email, encrypted_password,
    email_confirmed_at, raw_app_meta_data, raw_user_meta_data,
    created_at, updated_at,
    confirmation_token, recovery_token, email_change, email_change_token_new,
    phone_change, phone_change_token, email_change_token_current, reauthentication_token
) values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000501',
     'authenticated', 'authenticated', 'rehearsal-head@example.invalid', '',
     now(), '{"provider":"email","providers":["email"]}', '{"display_name":"Rehearsal Head"}',
     now() - interval '30 days', now() - interval '30 days',
     '', '', '', '', '', '', '', ''),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000502',
     'authenticated', 'authenticated', 'rehearsal-staff@example.invalid', '',
     now(), '{"provider":"email","providers":["email"]}', '{"display_name":"Rehearsal Staff"}',
     now() - interval '30 days', now() - interval '30 days',
     '', '', '', '', '', '', '', ''),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000503',
     'authenticated', 'authenticated', 'rehearsal-leaver@example.invalid', '',
     now(), '{"provider":"email","providers":["email"]}', '{"display_name":"Rehearsal Leaver"}',
     now() - interval '30 days', now() - interval '30 days',
     '', '', '', '', '', '', '', '');

-- handle_new_user has already created the three profiles with role 'staff'.
update public.profiles set role = 'admin'
where id = '00000000-0000-0000-0000-000000000501';
update public.profiles set is_active = false
where id = '00000000-0000-0000-0000-000000000503';

insert into public.announcements (id, org_id, title, body, expires_at, created_by, created_at) values
    ('00000000-0000-0000-0000-000000000511', '00000000-0000-0000-0000-000000000001',
     'Evergreen notice', 'Visible with no expiry.', null,
     '00000000-0000-0000-0000-000000000501', now() - interval '21 days'),
    ('00000000-0000-0000-0000-000000000512', '00000000-0000-0000-0000-000000000001',
     'Current notice', 'Visible until next month.', now() + interval '30 days',
     '00000000-0000-0000-0000-000000000501', now() - interval '7 days'),
    ('00000000-0000-0000-0000-000000000513', '00000000-0000-0000-0000-000000000001',
     'Expired notice', 'Expired last week but retained.', now() - interval '7 days',
     '00000000-0000-0000-0000-000000000502', now() - interval '14 days'),
    ('00000000-0000-0000-0000-000000000514', '00000000-0000-0000-0000-000000000001',
     'Leaver''s notice', 'Authored by a deactivated profile.', null,
     '00000000-0000-0000-0000-000000000503', now() - interval '20 days');

insert into public.date_overrides (id, org_id, date, timetable_id, note) values
    ('00000000-0000-0000-0000-000000000521', '00000000-0000-0000-0000-000000000001',
     current_date + 14, '00000000-0000-0000-0000-000000000100', 'Rehearsal open day');
