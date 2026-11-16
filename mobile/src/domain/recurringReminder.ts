import { formatLocalDate, getAllAssignedPeriods, parseTimeToMinutes } from './scheduleEngine';
import type { ScheduleSnapshot } from './scheduleTypes';

export type RecurringReminder = {
  id: string;
  title: string;
  body: string;
  weekdays: number; // Monday = bit 0, Sunday = bit 6.
  timing: 'lesson_end' | 'fixed_time';
  fixedTime: string | null;
  isEnabled: boolean;
};

export function getReminderOccurrences(snapshot: ScheduleSnapshot, date: Date) {
  const weekdayBit = 1 << ((date.getDay() + 6) % 7);
  // Staff select no classes, so use every timetable assigned that day rather than the viewer's own.
  const ends = [...new Set(getAllAssignedPeriods(snapshot, date)
    .filter((period) => period.isLesson)
    .map((period) => parseTimeToMinutes(period.endTime)))];
  return (snapshot.recurringReminders ?? []).flatMap((reminder) => {
    if (!reminder.isEnabled || !(reminder.weekdays & weekdayBit)) return [];
    const minutes = reminder.timing === 'lesson_end' ? ends
      : reminder.fixedTime ? [parseTimeToMinutes(reminder.fixedTime)] : [];
    return minutes.map((minute) => {
      const triggerTime = new Date(date.getFullYear(), date.getMonth(), date.getDate(),
        Math.floor(minute / 60), minute % 60);
      const timeKey = `${String(Math.floor(minute / 60)).padStart(2, '0')}${String(minute % 60).padStart(2, '0')}00`;
      return {
        identifier: `reminder:${reminder.id.replaceAll('-', '').toLowerCase()}:${formatLocalDate(date)}:${timeKey}`,
        triggerTime,
        title: reminder.title,
        body: reminder.body,
        kind: 'reminder' as const,
      };
    });
  });
}
