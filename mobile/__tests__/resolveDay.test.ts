import { resolveDay } from '@/src/domain/scheduleEngine';
import { EMPTY_SNAPSHOT, ScheduleSnapshot } from '@/src/domain/scheduleTypes';

import { id, localDate, monday, normalDay, override, period, timetable, week, weekOf } from '../test/testData';

describe('resolveDay', () => {
  it('gives an override precedence over the week schedule', () => {
    const normal = normalDay();
    const exam = timetable('Exam Day', period('Exam', '08:30', '12:00'));
    const snapshot: ScheduleSnapshot = {
      timetables: [normal, exam],
      weekSchedule: week([0, normal.id]),
      dateOverrides: [override(monday, exam.id)],
    };
    const day = resolveDay(snapshot, monday);
    expect(day.source).toBe('override');
    expect(day.timetable?.id).toBe(exam.id);
  });

  it('treats a closed override as no school even when assigned', () => {
    const day = resolveDay(weekOf(normalDay(), override(monday, null)), monday);
    expect(day).toMatchObject({ source: 'override', timetable: null, isSchoolDay: false, periods: [] });
  });

  it('uses the week schedule without an override', () => {
    const normal = normalDay();
    const day = resolveDay(weekOf(normal), monday);
    expect(day.source).toBe('week-schedule');
    expect(day.timetable?.id).toBe(normal.id);
    expect(day.isSchoolDay).toBe(true);
  });

  it('treats an unassigned weekday as no school', () => {
    const day = resolveDay(weekOf(normalDay()), localDate(2026, 7, 18));
    expect(day.source).toBe('none');
    expect(day.isSchoolDay).toBe(false);
  });

  it('treats a null weekday assignment as no school', () => {
    const day = resolveDay({ timetables: [normalDay()], weekSchedule: week([0, null]), dateOverrides: [] }, monday);
    expect(day.source).toBe('none');
    expect(day.isSchoolDay).toBe(false);
  });

  it('treats an override referencing a missing timetable as closed', () => {
    const day = resolveDay(weekOf(normalDay(), override(monday, id())), monday);
    expect(day).toMatchObject({ source: 'override', timetable: null, isSchoolDay: false });
  });

  it('keeps week-schedule source for a missing referenced timetable', () => {
    const day = resolveDay({ timetables: [], weekSchedule: week([0, id()]), dateOverrides: [] }, monday);
    expect(day).toMatchObject({ source: 'week-schedule', timetable: null, isSchoolDay: false });
  });

  it('uses canonical UUID ordering when multiple selected tracks match', () => {
    const lower = timetable('Lower', period('Lower lesson', '09:00', '10:00'));
    const higher = timetable('Higher', period('Higher lesson', '09:00', '10:00'));
    const lowClass = '7fffffff-0000-0000-0000-000000000001';
    const highClass = '80000000-0000-0000-0000-000000000002';
    const day = resolveDay({
      timetables: [lower, higher],
      weekSchedule: [
        { id: id(), weekday: 0, audienceClassId: highClass, timetableId: higher.id },
        { id: id(), weekday: 0, audienceClassId: lowClass, timetableId: lower.id },
      ],
      dateOverrides: [],
      viewerClassIds: new Set([highClass, lowClass]),
    }, monday);

    expect(day.timetable?.id).toBe(lower.id);
  });

  it('excludes invalid periods', () => {
    const item = timetable(
      'Bad Data Day',
      period('Fine', '09:00', '10:00', 1),
      period('Inverted', '11:00', '10:30', 2),
      period('Zero length', '12:00', '12:00', 3),
    );
    expect(resolveDay(weekOf(item), monday).periods.map((x) => x.name)).toEqual(['Fine']);
  });

  it('sorts periods by start time then sort order', () => {
    const item = timetable(
      'Unordered',
      period('C', '11:00', '12:00', 1),
      period('B2', '09:00', '09:45', 5),
      period('B1', '09:00', '10:00', 2),
      period('A', '08:00', '09:00', 9),
    );
    expect(resolveDay(weekOf(item), monday).periods.map((x) => x.name)).toEqual(['A', 'B1', 'B2', 'C']);
  });

  it('still resolves an archived timetable when referenced', () => {
    const archived = { ...timetable('Old Day', period('Period 1', '09:00', '10:00')), isArchived: true };
    expect(resolveDay(weekOf(archived), monday).isSchoolDay).toBe(true);
  });

  it('resolves an empty snapshot as no school', () => {
    expect(resolveDay(EMPTY_SNAPSHOT, monday)).toMatchObject({ source: 'none', isSchoolDay: false });
  });
});
