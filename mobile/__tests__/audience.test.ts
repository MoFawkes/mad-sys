import {
  DeviceAudience,
  filterScheduleForAudience,
  matchesAnnouncement,
  matchesPeriod,
} from '@/src/domain/audience';
import { ScheduleSnapshot } from '@/src/domain/scheduleTypes';

const student: DeviceAudience = {
  role: 'StudentDevice',
  selectedClassIds: new Set(['class-a']),
  optedHalfDays: new Set(['am']),
};

describe('audience predicates', () => {
  it('matches announcement audiences exactly', () => {
    expect(matchesAnnouncement(student, { audienceType: 'everyone' })).toBe(true);
    expect(matchesAnnouncement(student, { audienceType: 'teachers' })).toBe(false);
    expect(matchesAnnouncement(student, { audienceType: 'graduates' })).toBe(false);
    expect(matchesAnnouncement(student, { audienceType: 'am' })).toBe(true);
    expect(matchesAnnouncement(student, { audienceType: 'pm' })).toBe(false);
    expect(matchesAnnouncement(student, { audienceType: 'specific_class', audienceClassId: 'class-a' })).toBe(true);
    expect(matchesAnnouncement(student, { audienceType: 'specific_class', audienceClassId: 'class-b' })).toBe(false);
    expect(matchesAnnouncement(student, { audienceType: 'specific_class' })).toBe(false);
  });
  it('allows teacher and admin audiences to see teachers announcements', () => {
    for (const role of ['Teacher', 'Admin'] as const) {
      expect(
        matchesAnnouncement(
          { role, selectedClassIds: new Set(), optedHalfDays: new Set() },
          { audienceType: 'teachers' },
        ),
      ).toBe(true);
    }
  });
  it('hard-rejects graduates for every role', () => {
    expect(
      matchesAnnouncement(
        { role: 'Admin', selectedClassIds: new Set(), optedHalfDays: new Set() },
        { audienceType: 'graduates' },
      ),
    ).toBe(false);
  });
  it('requires class overlap for a student period only', () => {
    expect(matchesPeriod(student, new Set(['class-a', 'class-b']))).toBe(true);
    expect(matchesPeriod(student, new Set(['class-b']))).toBe(false);
    expect(matchesPeriod(student, new Set())).toBe(true);
    expect(
      matchesPeriod(
        { role: 'Teacher', selectedClassIds: new Set(), optedHalfDays: new Set() },
        new Set(),
      ),
    ).toBe(true);
  });
  it('keeps school-wide periods while filtering tagged lessons on the personal clock', () => {
    const snapshot: ScheduleSnapshot = {
      weekSchedule: {},
      dateOverrides: [],
      timetables: [
        {
          id: 'day',
          name: 'Day',
          isArchived: false,
          periods: [
            {
              id: 'break',
              name: 'Break',
              startTime: '10:00:00',
              endTime: '10:15:00',
              sortOrder: 1,
              isLesson: false,
              classIds: [],
            },
            {
              id: 'mine',
              name: 'My lesson',
              startTime: '10:15:00',
              endTime: '11:00:00',
              sortOrder: 2,
              isLesson: true,
              classIds: ['class-a'],
            },
            {
              id: 'other',
              name: 'Other lesson',
              startTime: '11:00:00',
              endTime: '11:45:00',
              sortOrder: 3,
              isLesson: true,
              classIds: ['class-b'],
            },
          ],
        },
      ],
    };

    expect(
      filterScheduleForAudience(snapshot, student).timetables[0].periods.map(
        (period) => period.id,
      ),
    ).toEqual(['break', 'mine']);
  });
});
