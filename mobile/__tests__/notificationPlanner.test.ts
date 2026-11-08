import {
  buildDesiredNotifications,
  cancelAllScheduledAqiClockNotifications,
  diffScheduledNotifications,
  NOTIFICATION_LIMIT,
  reconcileScheduledNotifications,
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
jest.mock('@/src/data/repositories', () => ({
  recordNotificationScheduleSnapshot: jest.fn(async () => undefined),
}));

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
  function sharedSnapshot(): ScheduleSnapshot {
    const base = snapshot(1);
    return {
      ...base,
      weekSchedule: [
        { id: 'mine', weekday: 0, audienceClassId: 'mine', timetableId: null },
        { id: 'other', weekday: 0, audienceClassId: 'other', timetableId: 'shared' },
        { id: 'copy', weekday: 0, audienceClassId: 'copy', timetableId: 'copy' },
      ],
      timetables: ['shared', 'copy', 'unassigned'].map((id) => ({
        id, name: id, isArchived: false,
        periods: [
          { ...base.timetables[0].periods[0], id: `${id}-am`, name: 'NASEEHAH & Break', startTime: id === 'unassigned' ? '09:00' : '11:00', endTime: '11:25', classIds: ['other'], isLesson: false },
          { ...base.timetables[0].periods[0], id: `${id}-pm`, name: 'Isha + Naseehah', startTime: id === 'unassigned' ? '18:00' : '12:00', endTime: id === 'unassigned' ? '18:25' : '12:25', classIds: ['other'], isLesson: false },
          { ...base.timetables[0].periods[0], id: `${id}-lesson`, name: 'Other class lesson', startTime: '10:00', endTime: '10:30', classIds: ['other'] },
        ],
      })),
    };
  }

  it.each([
    [[], []],
    [['am'], ['naseehah:2026-07-27:1100']],
    [['pm'], ['naseehah:2026-07-27:1200']],
    [['am', 'pm'], ['naseehah:2026-07-27:1100', 'naseehah:2026-07-27:1200']],
  ] as const)('shares reminders across classes for opt-ins %j', (opted, expected) => {
    const desired = buildDesiredNotifications(sharedSnapshot(),
      { ...student, optedHalfDays: new Set(opted) },
      { ...DEFAULT_NOTIFICATION_SETTINGS, lessonStartEnabled: false }, new Date(2026, 6, 27, 7));
    expect(desired.map((item) => item.identifier)).toEqual(expected);
    expect(desired.every((item) => item.title === 'Naseehah starting' && item.kind === 'naseehah')).toBe(true);
  });

  it('disables shared reminders with Announcement notifications and leaves class alerts intact', () => {
    const data = sharedSnapshot();
    const audience: DeviceAudience = { ...student, optedHalfDays: new Set(['am', 'pm']) };
    const settings = { ...DEFAULT_NOTIFICATION_SETTINGS, announcementsEnabled: false };
    expect(buildDesiredNotifications(data, audience, settings, new Date(2026, 6, 27, 7))).toEqual([]);
    const ownClass = { ...data, weekSchedule: [{ id: 'mine', weekday: 0, audienceClassId: 'mine', timetableId: 'shared' }],
      timetables: data.timetables.map((t) => ({ ...t, periods: t.periods.map((p) => ({ ...p, classIds: [] })) })) };
    const ownAlerts = buildDesiredNotifications(ownClass, audience, settings, new Date(2026, 6, 27, 7));
    expect(ownAlerts.filter((item) => item.kind === 'start')).toHaveLength(3);
    expect(ownAlerts.filter((item) => item.kind === 'naseehah')).toHaveLength(0);
  });

  it('replaces the matching class start with one shared reminder and keeps its end warning', () => {
    const data = sharedSnapshot();
    const ownClass = { ...data, weekSchedule: [{ id: 'mine', weekday: 0, audienceClassId: 'mine', timetableId: 'shared' }],
      timetables: data.timetables.map((t) => ({ ...t, periods: t.periods.slice(0, 1).map((p) => ({ ...p, classIds: [] })) })) };
    const desired = buildDesiredNotifications(ownClass, { ...student, optedHalfDays: new Set(['am']) },
      DEFAULT_NOTIFICATION_SETTINGS, new Date(2026, 6, 27, 7));
    expect(desired.map((item) => item.kind)).toEqual(['naseehah', 'end-warning']);
  });

  it('honours closure and replacement overrides for all shared sources', () => {
    const data = sharedSnapshot();
    const audience: DeviceAudience = { ...student, optedHalfDays: new Set(['am', 'pm']) };
    const closed = { ...data, dateOverrides: [{ id: 'closed', date: '2026-07-27', timetableId: null }] };
    expect(buildDesiredNotifications(closed, audience, DEFAULT_NOTIFICATION_SETTINGS, new Date(2026, 6, 27, 7))).toEqual([]);
    const changed = { ...data, dateOverrides: [{ id: 'replacement', date: '2026-07-27', timetableId: 'day' }],
      timetables: [...data.timetables, snapshot(1).timetables[0]] };
    expect(buildDesiredNotifications(changed, audience, DEFAULT_NOTIFICATION_SETTINGS, new Date(2026, 6, 27, 7))
      .some((item) => item.kind === 'naseehah')).toBe(false);
  });

  it('anchors shared reminders to institute time and drops reminders that already started', () => {
    const audience: DeviceAudience = { ...student, optedHalfDays: new Set(['am', 'pm']) };
    const desired = buildDesiredNotifications(sharedSnapshot(), audience, DEFAULT_NOTIFICATION_SETTINGS,
      new Date('2026-07-27T10:30:00Z'), 'Europe/London');
    expect(desired.map((item) => item.identifier)).toEqual(['naseehah:2026-07-27:1200']);
    expect(desired[0].triggerTime.toISOString()).toBe('2026-07-27T11:00:00.000Z');
  });

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

describe('notification schedule evidence', () => {
  it('captures the desired and actual sets at every reconcile', async () => {
    const Notifications = jest.requireMock('expo-notifications') as {
      getAllScheduledNotificationsAsync: jest.Mock;
    };
    const { recordNotificationScheduleSnapshot } = jest.requireMock(
      '@/src/data/repositories',
    ) as { recordNotificationScheduleSnapshot: jest.Mock };
    Notifications.getAllScheduledNotificationsAsync.mockResolvedValue([]);

    await reconcileScheduledNotifications(
      snapshot(1), student, DEFAULT_NOTIFICATION_SETTINGS,
      new Date(2026, 6, 27, 7, 0),
    );

    expect(recordNotificationScheduleSnapshot).toHaveBeenCalledTimes(1);
    const payload = JSON.parse(recordNotificationScheduleSnapshot.mock.calls[0][0]);
    expect(payload.desired.length).toBeGreaterThan(0);
    expect(payload.actual).toEqual([]);
  });
});

describe('notification cleanup', () => {
  it('cancels a stale shared reminder after opt-out or closure', async () => {
    const Notifications = jest.requireMock('expo-notifications') as {
      getAllScheduledNotificationsAsync: jest.Mock; cancelScheduledNotificationAsync: jest.Mock;
    };
    Notifications.getAllScheduledNotificationsAsync.mockResolvedValue([
      { identifier: 'naseehah:2026-07-27:1100', content: { title: 'Naseehah starting', body: 'Naseehah' } },
    ]);
    await reconcileScheduledNotifications({ timetables: [], weekSchedule: [], dateOverrides: [] },
      student, DEFAULT_NOTIFICATION_SETTINGS, new Date(2026, 6, 27, 7));
    expect(Notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith('naseehah:2026-07-27:1100');
  });

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
