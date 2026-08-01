import { findCurrentPeriod, parseTimeToMinutes, resolveDay } from '@/src/domain/scheduleEngine';

import { monday, period, timetable, weekOf } from '../test/testData';

describe('Postgres wall-clock time comparison', () => {
  it('parses HH:MM:SS into integer minutes since midnight', () => {
    expect(parseTimeToMinutes('00:00:00')).toBe(0);
    expect(parseTimeToMinutes('09:30:00')).toBe(570);
    expect(parseTimeToMinutes('23:59:59')).toBe(1439);
  });
  it('compares integer wall-clock values without constructing dates', () => {
    const day = resolveDay(
      weekOf(timetable('Wall clock', period('Lesson', '09:00:00', '10:00:00'))),
      monday,
    );
    expect(findCurrentPeriod(day, 9 * 60)).not.toBeNull();
    expect(findCurrentPeriod(day, 10 * 60)).toBeNull();
  });
  it('rejects malformed and out-of-range times', () => {
    expect(() => parseTimeToMinutes('9:00')).toThrow(RangeError);
    expect(() => parseTimeToMinutes('24:00:00')).toThrow(RangeError);
  });
});
