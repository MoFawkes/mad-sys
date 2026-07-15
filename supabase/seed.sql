insert into public.organizations (id, name, timezone)
values ('00000000-0000-0000-0000-000000000001', 'AQI', 'Europe/London')
on conflict (id) do nothing;

insert into public.timetables (id, org_id, name, is_archived)
values (
    '00000000-0000-0000-0000-000000000100',
    '00000000-0000-0000-0000-000000000001',
    'Normal Day',
    false
)
on conflict (id) do nothing;

insert into public.week_schedule (id, org_id, weekday, timetable_id)
select
    ('00000000-0000-0000-0000-' || lpad((200 + weekday)::text, 12, '0'))::uuid,
    '00000000-0000-0000-0000-000000000001'::uuid,
    weekday,
    null
from generate_series(0, 6) as weekday
on conflict (org_id, weekday) do nothing;

insert into public.periods (
    id, timetable_id, name, start_time, end_time, sort_order, is_lesson
)
values
    ('00000000-0000-0000-0000-000000000301', '00000000-0000-0000-0000-000000000100', 'Registration', '08:25', '08:45', 1, true),
    ('00000000-0000-0000-0000-000000000302', '00000000-0000-0000-0000-000000000100', 'Period 1', '08:45', '09:45', 2, true),
    ('00000000-0000-0000-0000-000000000303', '00000000-0000-0000-0000-000000000100', 'Break', '09:45', '10:05', 3, false)
on conflict (id) do nothing;
