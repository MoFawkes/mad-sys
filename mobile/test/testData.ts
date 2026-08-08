import {
  DateOverride,
  Period,
  ScheduleSnapshot,
  Timetable,
  WeekSchedule,
} from '@/src/domain/scheduleTypes';

export const monday = localDate(2026, 7, 13);
export const tuesday = localDate(2026, 7, 14);

let nextId = 1;
export function id(): string {
  return `00000000-0000-0000-0000-${(nextId++).toString().padStart(12, '0')}`;
}

export function period(
  name: string,
  startTime: string,
  endTime: string,
  sortOrder = 0,
  isLesson = true,
  periodId = id(),
): Period {
  return { id: periodId, name, startTime, endTime, sortOrder, isLesson };
}

export function timetable(name: string, ...periods: Period[]): Timetable {
  return { id: id(), name, isArchived: false, periods };
}

export function normalDay(): Timetable {
  return timetable(
    'Normal Day',
    period('Period 1', '09:00', '10:00', 1),
    period('Break', '10:00', '10:20', 2, false),
    period('Period 2', '10:20', '11:20', 3),
  );
}

export function weekOf(item: Timetable, ...dateOverrides: DateOverride[]): ScheduleSnapshot {
  return {
    timetables: [item],
    weekSchedule: week([0, item.id], [1, item.id], [2, item.id], [3, item.id], [4, item.id]),
    dateOverrides,
  };
}

export function week(...entries: readonly [number, string | null][]): WeekSchedule {
  return entries.map(([weekday, timetableId], index) => ({ id: `week-${weekday}-${index}`, weekday, audienceClassId: null, timetableId }));
}

export function override(date: Date, timetableId: string | null): DateOverride {
  return { id: id(), date: dateKey(date), timetableId };
}

export function at(date: Date, time: string): Date {
  const [hours, minutes, seconds = 0] = time.split(':').map(Number);
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), hours, minutes, seconds);
}

export function localDate(year: number, month: number, day: number): Date {
  return new Date(year, month - 1, day);
}

export function dateKey(date: Date): string {
  return `${date.getFullYear().toString().padStart(4, '0')}-${(date.getMonth() + 1)
    .toString()
    .padStart(2, '0')}-${date.getDate().toString().padStart(2, '0')}`;
}
