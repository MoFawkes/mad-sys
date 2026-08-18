import { buildLessonStatus, getStatus } from '@/src/domain/scheduleEngine';

import { at, localDate, monday, normalDay, override, period, timetable, tuesday, week, weekOf } from '../test/testData';

describe('getStatus', () => {
  it('reports remaining time and progress during a lesson', () => {
    const status = getStatus(weekOf(normalDay()), at(monday, '09:45'));
    expect(status.current?.period.name).toBe('Period 1');
    expect(status.next?.period.name).toBe('Break');
    expect(status.timeRemainingMs).toBe(15 * 60_000);
    expect(status.progress).toBeCloseTo(0.75, 10);
  });
  it('has null remaining and progress outside lessons', () => {
    const status = getStatus(weekOf(normalDay()), at(monday, '07:00'));
    expect(status.current).toBeNull();
    expect(status.timeRemainingMs).toBeNull();
    expect(status.progress).toBeNull();
    expect(status.next?.period.name).toBe('Period 1');
  });
  it('uses wall-clock seconds for the foreground countdown', () => {
    const status = getStatus(weekOf(normalDay()), at(monday, '09:45:30'));
    expect(status.timeRemainingMs).toBe(14.5 * 60_000);
  });
  it('clamps remaining time at zero and progress at one', () => {
    const item = period('Period 1', '09:00', '10:00');
    const occurrence = {
      date: monday,
      period: item,
      startsAt: at(monday, '09:00'),
      endsAt: at(monday, '10:00'),
    };
    const day = {
      date: monday,
      timetable: null,
      timetables: [],
      source: 'week-schedule' as const,
      periods: [item],
      scheduledPeriods: [{ period: item, classId: null }],
      isSchoolDay: true,
    };
    const status = buildLessonStatus(at(monday, '10:00:30'), day, occurrence, null);
    expect(status.timeRemainingMs).toBe(0);
    expect(status.progress).toBe(1);
  });
  it('points a closed day at the next school day', () => {
    const status = getStatus(weekOf(normalDay(), override(monday, null)), at(monday, '09:30'));
    expect(status.day.isSchoolDay).toBe(false);
    expect(status.current).toBeNull();
    expect(status.next?.date).toEqual(tuesday);
  });
  it('uses wall-clock arithmetic across a DST transition date', () => {
    const springForward = localDate(2026, 3, 29);
    const item = timetable('Early', period('Fajr class', '00:30', '03:30'));
    const status = getStatus(
      { timetables: [item], weekSchedule: week([6, item.id]), dateOverrides: [] },
      at(springForward, '03:00'),
    );
    expect(status.current?.period.name).toBe('Fajr class');
    expect(status.timeRemainingMs).toBe(30 * 60_000);
  });
  it('purely recomputes a mid-day edit from the new snapshot', () => {
    const now = at(monday, '09:30');
    const after = timetable('Edited Day', period('Period 2', '10:20', '11:20'));
    expect(getStatus(weekOf(normalDay()), now).current?.period.name).toBe('Period 1');
    expect(getStatus(weekOf(after), now).current).toBeNull();
    expect(getStatus(weekOf(after), now).next?.period.name).toBe('Period 2');
  });
});
