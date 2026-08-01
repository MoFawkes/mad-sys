import { findNextPeriod, LOOKAHEAD_DAYS } from '@/src/domain/scheduleEngine';
import { EMPTY_SNAPSHOT, ScheduleSnapshot } from '@/src/domain/scheduleTypes';

import { at, id, localDate, monday, normalDay, override, period, timetable, tuesday, weekOf } from '../test/testData';

describe('findNextPeriod', () => {
  it('returns the first period today before school', () => {
    const next = findNextPeriod(weekOf(normalDay()), at(monday, '07:30'));
    expect(next?.period.name).toBe('Period 1');
    expect(next?.date).toEqual(monday);
  });
  it('returns the following period during a period', () => expect(findNextPeriod(weekOf(normalDay()), at(monday, '09:30'))?.period.name).toBe('Break'));
  it('does not count a period starting exactly now as next', () => expect(findNextPeriod(weekOf(normalDay()), at(monday, '09:00'))?.period.name).toBe('Break'));
  it('moves to the next school day after the last period', () => expect(findNextPeriod(weekOf(normalDay()), at(monday, '15:00'))?.date).toEqual(tuesday));
  it('scans across a weekend', () => expect(findNextPeriod(weekOf(normalDay()), at(localDate(2026, 7, 17), '18:00'))?.date).toEqual(localDate(2026, 7, 20)));
  it('skips closed override days', () => expect(findNextPeriod(weekOf(normalDay(), override(tuesday, null)), at(monday, '15:00'))?.date).toEqual(localDate(2026, 7, 15)));
  it('skips days with no valid periods', () => {
    const normal = normalDay();
    const broken = timetable('Broken', period('Inverted', '11:00', '09:00'));
    const snapshot: ScheduleSnapshot = { timetables: [normal, broken], weekSchedule: { 0: normal.id, 1: broken.id, 2: normal.id }, dateOverrides: [] };
    expect(findNextPeriod(snapshot, at(monday, '15:00'))?.date).toEqual(localDate(2026, 7, 15));
  });
  it('uses a future override timetable', () => {
    const normal = normalDay();
    const exam = timetable('Exam Day', period('Exam', '08:30', '12:00'));
    const snapshot: ScheduleSnapshot = { timetables: [normal, exam], weekSchedule: { 0: normal.id }, dateOverrides: [override(tuesday, exam.id)] };
    expect(findNextPeriod(snapshot, at(monday, '15:00'))).toMatchObject({ date: tuesday, period: { name: 'Exam' } });
  });
  it('returns none for an empty schedule', () => expect(findNextPeriod(EMPTY_SNAPSHOT, at(monday, '09:00'))).toBeNull());
  it('finds a lesson exactly at the lookahead limit', () => {
    const normal = normalDay();
    const far = localDate(2026, 7, 13 + LOOKAHEAD_DAYS);
    const snapshot: ScheduleSnapshot = { timetables: [normal], weekSchedule: {}, dateOverrides: [override(far, normal.id)] };
    expect(findNextPeriod(snapshot, at(monday, '09:00'))?.date).toEqual(far);
  });
  it('does not find a lesson beyond the lookahead limit', () => {
    const normal = normalDay();
    const tooFar = localDate(2026, 7, 13 + LOOKAHEAD_DAYS + 1);
    const snapshot: ScheduleSnapshot = { timetables: [normal], weekSchedule: {}, dateOverrides: [{ id: id(), date: `${tooFar.getFullYear()}-${String(tooFar.getMonth()+1).padStart(2,'0')}-${String(tooFar.getDate()).padStart(2,'0')}`, timetableId: normal.id }] };
    expect(findNextPeriod(snapshot, at(monday, '09:00'))).toBeNull();
  });
});
