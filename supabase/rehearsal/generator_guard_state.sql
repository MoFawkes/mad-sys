-- One generated timetable is enough to make the forward transition fail
-- closed. No generator definition is needed for this guard case.
insert into public.organizations (id, name, timezone) values
    ('00000000-0000-0000-0000-000000009501',
     'Generator guard rehearsal', 'Europe/London');

insert into public.timetables (id, org_id, name, is_archived, is_generated) values
    ('00000000-0000-0000-0000-000000009502',
     '00000000-0000-0000-0000-000000009501',
     'Must block forward migration', false, true);
