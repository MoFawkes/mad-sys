import { findCurrentPeriod, resolveDay } from '@/src/domain/scheduleEngine';
import { Timetable } from '@/src/domain/scheduleTypes';

import { monday, normalDay, override, period, timetable, weekOf } from '../test/testData';

function currentAt(item: Timetable, time: string) {
  return findCurrentPeriod(resolveDay(weekOf(item), monday), time);
}

describe('findCurrentPeriod', () => {
  it('returns no current period before the first', () => expect(currentAt(normalDay(), '08:59')).toBeNull());
  it('includes the exact start', () => expect(currentAt(normalDay(), '09:00')?.period.name).toBe('Period 1'));
  it('finds a period midway', () => expect(currentAt(normalDay(), '09:30')?.period.name).toBe('Period 1'));
  it('excludes an exact end and includes the next start', () => expect(currentAt(normalDay(), '10:00')?.period.name).toBe('Break'));
  it('returns none at the last exact end', () => expect(currentAt(normalDay(), '11:20')).toBeNull());
  it('returns none in a gap', () => {
    const item = timetable('Gappy', period('Period 1', '09:00', '10:00', 1), period('Period 2', '10:30', '11:30', 2));
    expect(currentAt(item, '10:15')).toBeNull();
  });
  it('chooses the latest start when periods overlap', () => {
    const item = timetable('Overlap', period('Long block', '09:00', '12:00', 1), period('Intervention', '10:00', '10:30', 2));
    expect(currentAt(item, '10:15')?.period.name).toBe('Intervention');
    expect(currentAt(item, '11:00')?.period.name).toBe('Long block');
  });
  it('chooses the lowest sort order when overlapping starts tie', () => {
    const item = timetable('Parallel', period('Stream B', '09:00', '10:00', 2), period('Stream A', '09:00', '10:00', 1));
    expect(currentAt(item, '09:30')?.period.name).toBe('Stream A');
  });
  it('returns none on a closed day', () => {
    const day = resolveDay(weekOf(normalDay(), override(monday, null)), monday);
    expect(findCurrentPeriod(day, '09:30')).toBeNull();
  });
});
