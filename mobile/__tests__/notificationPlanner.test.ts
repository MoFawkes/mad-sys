import {
  buildDesiredNotifications,
  cancelAllScheduledAqiClockNotifications,
  diffScheduledNotifications,
  NOTIFICATION_LIMIT,
} from '@/src/notifications/planner';
import {
  DEFAULT_NOTIFICATION_SETTINGS,
  normalizeNotificationSettings,
} from '@/src/notifications/settings';
import { DeviceAudience, ScheduleSnapshot } from '@/src/domain';

jest.mock('expo-notifications', () => ({
  getAllScheduledNotificationsAsync: jest.fn(),
  cancelScheduledNotificationAsync: jest.fn(),
  cancelAllScheduledNotificationsAsync: jest.fn(),
  scheduleNotificationAsync: jest.fn(),
  SchedulableTriggerInputTypes: { DATE: 'date' },
}));
jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));

const student: DeviceAudience = {
  role: 'StudentDevice',
  selectedClassIds: new Set(['mine']),
  optedHalfDays: new Set(),
};

function snapshot(periodCount = 2): ScheduleSnapshot {
  return {
    weekSchedule: Array.from({ length: 7 }, (_, weekday) => ({ id: `week-${weekday}`, weekday, audienceClassId: null, timetableId: 'day' })),
    dateOverrides: [],
    timetables: [
      {
        id: 'day',
        name: 'Day',
        isArchived: false,
        periods: Array.from({ length: periodCount }, (_, index) => {
          const hour = 8 + Math.floor(index / 2);
          const minute = index % 2 === 0 ? 0 : 30;
          const endMinute = minute === 0 ? 30 : 0;
          const endHour = minute === 0 ? hour : hour + 1;
          return {
            id: `00000000-0000-0000-0000-${(index + 1).toString().padStart(12, '0')}`,
            name: `Period ${index + 1}`,
            startTime: `${hour.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}:00`,
            endTime: `${endHour.toString().padStart(2, '0')}:${endMinute.toString().padStart(2, '0')}:00`,
            sortOrder: index,
            isLesson: true,
            classIds: index === 1 ? ['other'] : index === 0 ? [] : ['mine'],
          };
        }),
      },
    ],
  };
}

describe('notification desired set', () => {
  it('keeps untagged school-wide periods and excludes another class', () => {
    const desired = buildDesiredNotifications(
      snapshot(),
      student,
      DEFAULT_NOTIFICATION_SETTINGS,
      new Date(2026, 6, 27, 7, 0),
    );

    expect(desired.map((item) => item.identifier)).toEqual(
      expect.arrayContaining([
        'start:00000000000000000000000000000001:2026-07-27',
        'end-warning:00000000000000000000000000000001:2026-07-27',
      ]),
    );
    expect(desired.some((item) => item.identifier.includes('000000000002'))).toBe(false);
  });

  it('sorts by trigger and truncates below the iOS pending limit', () => {
    const desired = buildDesiredNotifications(
      snapshot(20),
      { role: 'Teacher', selectedClassIds: new Set(), optedHalfDays: new Set() },
      DEFAULT_NOTIFICATION_SETTINGS,
      new Date(2026, 6, 27, 7, 0),
    );

    expect(desired).toHaveLength(NOTIFICATION_LIMIT);
    expect(
      desired.every(
        (item, index) =>
          index === 0 ||
          desired[index - 1].triggerTime.getTime() <= item.triggerTime.getTime(),
      ),
    ).toBe(true);
  });

  it('honours per-kind settings', () => {
    const desired = buildDesiredNotifications(
      snapshot(1),
      student,
      { ...DEFAULT_NOTIFICATION_SETTINGS, lessonStartEnabled: false },
      new Date(2026, 6, 27, 7, 0),
    );
    expect(desired.every((item) => item.kind === 'end-warning')).toBe(true);
  });

  it('merges simultaneous events under the lowest period identifier', () => {
    const data = snapshot(2);
    const periods = data.timetables[0].periods.map((item) => ({
      ...item, startTime: '08:00:00', endTime: '08:30:00', classIds: [],
    }));
    const desired = buildDesiredNotifications(
      { ...data, timetables: [{ ...data.timetables[0], periods }] }, student,
      DEFAULT_NOTIFICATION_SETTINGS, new Date(2026, 6, 27, 7, 0),
    );

    const starts = desired.filter((item) => item.kind === 'start');
    expect(starts).toHaveLength(7);
    expect(starts[0].identifier).toContain('00000000000000000000000000000001');
    expect(starts[0].body).toBe('Period 1\nPeriod 2');
  });

  it('anchors lesson triggers to the institute timezone', () => {
    const desired = buildDesiredNotifications(
      snapshot(1), student, DEFAULT_NOTIFICATION_SETTINGS,
      new Date('2026-07-27T06:00:00.000Z'), 'Europe/London',
    );
    expect(desired.find((item) => item.kind === 'start')?.triggerTime.toISOString()).toBe('2026-07-27T07:00:00.000Z');
  });
});

describe('notification set diff', () => {
  const wanted = {
    identifier: 'start:item:2026-07-27',
    triggerTime: new Date('2026-07-27T08:00:00.000Z'),
    title: 'Start',
    body: 'Lesson',
    kind: 'start' as const,
  };

  it('reschedules unchanged times when notification text changes', () => {
    expect(diffScheduledNotifications([wanted], [{
      identifier: wanted.identifier,
      triggerTimeMs: wanted.triggerTime.getTime(),
      title: wanted.title,
      body: 'Old lesson name',
    }])).toEqual({ cancel: [wanted.identifier], schedule: [wanted] });
  });

  it('does nothing when identifier and trigger match', () => {
    expect(
      diffScheduledNotifications(
        [wanted],
        [{
          identifier: wanted.identifier,
          triggerTimeMs: wanted.triggerTime.getTime(),
          title: wanted.title,
          body: wanted.body,
        }],
      ),
    ).toEqual({ cancel: [], schedule: [] });
  });

  it('cancels and reschedules when a trigger moves', () => {
    expect(
      diffScheduledNotifications(
        [wanted],
        [{ identifier: wanted.identifier, triggerTimeMs: wanted.triggerTime.getTime() - 60_000, title: wanted.title, body: wanted.body }],
      ),
    ).toEqual({ cancel: [wanted.identifier], schedule: [wanted] });
  });

  it('cancels stale identifiers and schedules missing identifiers', () => {
    expect(
      diffScheduledNotifications(
        [wanted],
        [{ identifier: 'start:old:2026-07-27', triggerTimeMs: 1, title: 'Start', body: 'Old' }],
      ),
    ).toEqual({
      cancel: ['start:old:2026-07-27'],
      schedule: [wanted],
    });
  });
});

describe('notification cleanup', () => {
  it('cancels every app-owned scheduled notification on sign-out', async () => {
    const Notifications = jest.requireMock('expo-notifications') as {
      cancelAllScheduledNotificationsAsync: jest.Mock;
    };

    await cancelAllScheduledAqiClockNotifications();

    expect(Notifications.cancelAllScheduledNotificationsAsync).toHaveBeenCalledTimes(1);
  });
});

describe('notification settings', () => {
  it('clamps end-warning minutes to the desktop 0-15 range', () => {
    expect(normalizeNotificationSettings({ endWarningMinutes: -4 }).endWarningMinutes).toBe(0);
    expect(normalizeNotificationSettings({ endWarningMinutes: 99 }).endWarningMinutes).toBe(15);
  });
});
