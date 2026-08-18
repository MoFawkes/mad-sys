export type Period = {
  id: string;
  name: string;
  startTime: string;
  endTime: string;
  sortOrder: number;
  isLesson: boolean;
  classIds?: readonly string[];
};

export type Timetable = {
  id: string;
  name: string;
  isArchived: boolean;
  periods: readonly Period[];
};

export type WeekScheduleEntry = { id: string; weekday: number; audienceClassId: string | null; timetableId: string | null };
export type WeekSchedule = readonly WeekScheduleEntry[];

export type DateOverride = {
  id: string;
  date: string;
  timetableId: string | null;
  note?: string | null;
};

export type ScheduleSnapshot = {
  timetables: readonly Timetable[];
  weekSchedule: WeekSchedule;
  dateOverrides: readonly DateOverride[];
  viewerClassIds?: ReadonlySet<string>;
};

export type EffectiveDaySource = 'none' | 'week-schedule' | 'override';

export type EffectiveDay = {
  date: Date;
  timetable: Timetable | null;
  timetables: readonly Timetable[];
  source: EffectiveDaySource;
  periods: readonly Period[];
  scheduledPeriods: readonly ScheduledPeriod[];
  isSchoolDay: boolean;
};

export type ScheduledPeriod = { period: Period; classId: string | null };

export type PeriodOccurrence = {
  date: Date;
  period: Period;
  startsAt: Date;
  endsAt: Date;
};

export type NotificationEventKind = 'start' | 'end-warning';

export type NotificationEvent = {
  key: string;
  kind: NotificationEventKind;
  occurrence: PeriodOccurrence;
  triggerTime: Date;
};

export type LessonStatus = {
  timestamp: Date;
  day: EffectiveDay;
  current: PeriodOccurrence | null;
  next: PeriodOccurrence | null;
  timeRemainingMs: number | null;
  progress: number | null;
};

export const EMPTY_SNAPSHOT: ScheduleSnapshot = {
  timetables: [],
  weekSchedule: [],
  dateOverrides: [],
};
