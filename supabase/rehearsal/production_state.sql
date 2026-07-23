-- Production-like data for the incremental-migration rehearsal.
-- Runs against the RELEASED baseline schema (migrations up to 20260716000300
-- plus seed.sql), i.e. the state the production database is in on v0.9.6:
-- profiles still use role 'staff' and announcements have no audience columns.
-- Emails deliberately avoid the 'aqitest-' prefix that SupabaseFixture deletes.

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
