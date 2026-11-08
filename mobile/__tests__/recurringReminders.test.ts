import { buildDesiredNotifications, diffScheduledNotifications, reconcileScheduledNotifications } from '@/src/notifications/planner';
import { DEFAULT_NOTIFICATION_SETTINGS } from '@/src/notifications/settings';
import { DeviceAudience, RecurringReminder, ScheduleSnapshot } from '@/src/domain';

jest.mock('expo-notifications', () => ({
  getAllScheduledNotificationsAsync: jest.fn(async () => []),
  cancelScheduledNotificationAsync: jest.fn(),
  scheduleNotificationAsync: jest.fn(),
  SchedulableTriggerInputTypes: { DATE: 'date' },
}));
jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));
jest.mock('@/src/data/repositories', () => ({ recordNotificationScheduleSnapshot: jest.fn() }));

const teacher: DeviceAudience = { role: 'Teacher', selectedClassIds: new Set(), optedHalfDays: new Set() };
const rule: RecurringReminder = { id: 'chart', title: 'Week chart', body: 'Fill in the chart', weekdays: 16,
  timing: 'lesson_end', fixedTime: null, isEnabled: true };
const friday = new Date(2026, 10, 13, 7);
const settings = { ...DEFAULT_NOTIFICATION_SETTINGS, lessonStartEnabled: false, endWarningEnabled: false };
function snapshot(reminders: RecurringReminder[] = [rule]): ScheduleSnapshot {
  return {
    recurringReminders: reminders,
    weekSchedule: Array.from({ length: 7 }, (_, weekday) => ({ id: String(weekday), weekday, audienceClassId: null, timetableId: 'day' })),
    dateOverrides: [],
    timetables: [{ id: 'day', name: 'Normal', isArchived: false, periods: [
      { id: 'one', name: 'Lesson 1', startTime: '09:00', endTime: '10:00', sortOrder: 0, isLesson: true },
      { id: 'parallel', name: 'Parallel lesson', startTime: '09:00', endTime: '10:00', sortOrder: 1, isLesson: true },
      { id: 'break', name: 'Break', startTime: '10:00', endTime: '10:15', sortOrder: 2, isLesson: false },
      { id: 'two', name: 'Lesson 2', startTime: '10:15', endTime: '11:00', sortOrder: 3, isLesson: true },
    ] }],
  };
}

it('plans every Friday lesson end, skips breaks and deduplicates simultaneous ends', () => {
  const desired = buildDesiredNotifications(snapshot(), teacher, settings, friday);
  expect(desired.map((item) => item.triggerTime)).toEqual([new Date(2026, 10, 13, 10), new Date(2026, 10, 13, 11)]);
  expect(desired.map((item) => item.identifier)).toEqual(['reminder:chart:2026-11-13:100000', 'reminder:chart:2026-11-13:110000']);
  expect(buildDesiredNotifications(snapshot(), teacher, settings, new Date(2026, 10, 13, 10, 30))).toHaveLength(1);
});

it('follows date replacements and closures', () => {
  const closed = { ...snapshot(), dateOverrides: [{ id: 'closed', date: '2026-11-13', timetableId: null }] };
  expect(buildDesiredNotifications(closed, teacher, settings, friday)).toEqual([]);
  const changed = snapshot();
  changed.timetables = [...changed.timetables, { id: 'short', name: 'Short', isArchived: false, periods: [
    { id: 'short-one', name: 'Short lesson', startTime: '08:00', endTime: '08:30', sortOrder: 0, isLesson: true },
  ] }];
  changed.dateOverrides = [{ id: 'change', date: '2026-11-13', timetableId: 'short' }];
  expect(buildDesiredNotifications(changed, teacher, settings, friday).map((item) => item.triggerTime)).toEqual([new Date(2026, 10, 13, 8, 30)]);
});

it('excludes paused rules, students and disabled announcement notifications', () => {
  expect(buildDesiredNotifications(snapshot([{ ...rule, isEnabled: false }]), teacher, settings, friday)).toEqual([]);
  expect(buildDesiredNotifications(snapshot(), { ...teacher, role: 'StudentDevice' }, settings, friday)).toEqual([]);
  expect(buildDesiredNotifications(snapshot(), teacher, { ...settings, announcementsEnabled: false }, friday)).toEqual([]);
});

it('uses institute time for calendar reminders even on closed dates and keeps separate messages', () => {
  const fixed = { ...rule, timing: 'fixed_time' as const, fixedTime: '16:00:00' };
  const input = { ...snapshot([fixed, { ...fixed, id: 'second', title: 'Another reminder' }]),
    dateOverrides: [{ id: 'closed', date: '2026-11-13', timetableId: null }] };
  const desired = buildDesiredNotifications(input, teacher, settings, new Date('2026-11-13T01:00:00Z'), 'Asia/Karachi');
  expect(desired).toHaveLength(2);
  expect(desired[0].triggerTime.toISOString()).toBe('2026-11-13T11:00:00.000Z');
  expect(desired[0].title).not.toBe(desired[1].title);
});

it('reschedules changed endings and cancels a paused or deleted reminder', async () => {
  const desired = buildDesiredNotifications(snapshot(), teacher, settings, friday);
  const actual = desired.map((item) => ({ ...item, triggerTimeMs: item.triggerTime.getTime() }));
  expect(diffScheduledNotifications(desired, actual)).toEqual({ cancel: [], schedule: [] });
  const retimed = desired.map((item) => ({ ...item, triggerTime: new Date(item.triggerTime.getTime() + 60000) }));
  expect(diffScheduledNotifications(retimed, actual).cancel).toHaveLength(2);
  const Notifications = jest.requireMock('expo-notifications');
  Notifications.getAllScheduledNotificationsAsync.mockResolvedValue(desired.map((item) => ({
    identifier: item.identifier, content: { title: item.title, body: item.body, data: { aqiTriggerTime: item.triggerTime.getTime() } },
  })));
  await reconcileScheduledNotifications(snapshot([]), teacher, settings, friday);
  expect(Notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith(desired[0].identifier);
  expect(Notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith(desired[1].identifier);
});
