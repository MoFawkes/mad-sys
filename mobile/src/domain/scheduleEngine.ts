import {
  EffectiveDay,
  LessonStatus,
  NotificationEvent,
  Period,
  PeriodOccurrence,
  ScheduleSnapshot,
  Timetable,
  WeekScheduleEntry,
} from './scheduleTypes';
import { jsDayToDbWeekday } from './weekday';

export const LOOKAHEAD_DAYS = 60;

export function parseTimeToMinutes(value: string): number {
  const match = /^(\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?$/.exec(value);
  if (!match) {
    throw new RangeError(`Invalid Postgres time: ${value}`);
  }

  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  const seconds = Number(match[3] ?? 0);
  if (hours > 23 || minutes > 59 || seconds > 59) {
    throw new RangeError(`Invalid Postgres time: ${value}`);
  }

  // Minute granularity is intentional: the timetable editor cannot create second-level periods.
  return hours * 60 + minutes;
}

export function resolveDay(snapshot: ScheduleSnapshot, date: Date): EffectiveDay {
  const dateOnly = startOfLocalDay(date);
  const key = formatLocalDate(dateOnly);
  const override = findLast(snapshot.dateOverrides, (item) => item.date === key);

  let source: EffectiveDay['source'];
  let timetables: Timetable[];
  let resolvedEntries: WeekScheduleEntry[] = [];
  let resolvedSources: { timetable: Timetable; classId: string | null }[] = [];
  if (override) {
    source = 'override';
    const timetable = override.timetableId ? snapshot.timetables.find((item) => item.id === override.timetableId) : undefined;
    timetables = timetable ? [timetable] : [];
    resolvedSources = timetable ? [{ timetable, classId: null }] : [];
  } else {
    resolvedEntries = resolveWeekEntries(snapshot, jsDayToDbWeekday(dateOnly));
    const rawSources = resolvedEntries.flatMap((entry) => {
      const timetable = entry.timetableId ? snapshot.timetables.find((item) => item.id === entry.timetableId) : undefined;
      return timetable ? [{ timetable, classId: entry.audienceClassId }] : [];
    });
    resolvedSources = dedupeTimetableSources(rawSources);
    timetables = resolvedSources.map((item) => item.timetable);
    source = resolvedEntries.some((entry) => entry.timetableId != null) ? 'week-schedule' : 'none';
  }

  const scheduledPeriods = resolvedSources
    .flatMap(({ timetable, classId }) => timetable.periods.filter(isValidPeriod).map((period) => ({
      period,
      classId,
    })))
    .sort(
          (left, right) =>
            parseTimeToMinutes(left.period.startTime) - parseTimeToMinutes(right.period.startTime) ||
            left.period.sortOrder - right.period.sortOrder || ordinalCompare(left.period.id, right.period.id),
        );
  const periods = scheduledPeriods.map((item) => item.period);

  return {
    date: dateOnly,
    timetable: timetables[0] ?? null,
    timetables,
    source,
    periods,
    scheduledPeriods,
    isSchoolDay: periods.length > 0,
  };
}

export function resolveWeekEntry(snapshot: ScheduleSnapshot, weekday: number): WeekScheduleEntry | undefined {
  return resolveWeekEntries(snapshot, weekday)[0];
}

function dedupeTimetableSources(
  sources: readonly { timetable: Timetable; classId: string | null }[],
): { timetable: Timetable; classId: string | null }[] {
  const grouped = new Map<string, { timetable: Timetable; classIds: Set<string | null> }>();
  for (const source of sources) {
    const existing = grouped.get(source.timetable.id);
    if (existing) existing.classIds.add(source.classId);
    else grouped.set(source.timetable.id, { timetable: source.timetable, classIds: new Set([source.classId]) });
  }
  return [...grouped.values()].map(({ timetable, classIds }) => ({
    timetable,
    classId: classIds.size === 1 ? [...classIds][0] : null,
  }));
}

export function resolveWeekEntries(snapshot: ScheduleSnapshot, weekday: number): WeekScheduleEntry[] {
  const classes = snapshot.viewerClassIds ?? new Set<string>();
  const matches = snapshot.weekSchedule
    .filter((entry) => entry.weekday === weekday && entry.audienceClassId != null && classes.has(entry.audienceClassId))
    .sort((left, right) => ordinalCompare(left.audienceClassId!, right.audienceClassId!));
  if (matches.length > 0) return matches;
  const fallback = snapshot.weekSchedule.find((entry) => entry.weekday === weekday && entry.audienceClassId == null);
  return fallback ? [fallback] : [];
}

export function findCurrentPeriod(day: EffectiveDay, time: string | number): PeriodOccurrence | null {
  const at = typeof time === 'number' ? time : parseTimeToMinutes(time);
  let best: Period | null = null;

  for (const period of day.periods) {
    const start = parseTimeToMinutes(period.startTime);
    const end = parseTimeToMinutes(period.endTime);
    if (!(start <= at && at < end)) {
      continue;
    }

    if (
      !best ||
      start > parseTimeToMinutes(best.startTime) ||
      (start === parseTimeToMinutes(best.startTime) && period.sortOrder < best.sortOrder)
    ) {
      best = period;
    }
  }

  return best ? occurrence(day.date, best) : null;
}

export function findNextPeriod(snapshot: ScheduleSnapshot, after: Date): PeriodOccurrence | null {
  const date = startOfLocalDay(after);
  const time = wallClockMinutes(after);
  const today = resolveDay(snapshot, date);
  const nextToday = today.periods.find((period) => parseTimeToMinutes(period.startTime) > time);
  if (nextToday) {
    return occurrence(date, nextToday);
  }

  for (let daysAhead = 1; daysAhead <= LOOKAHEAD_DAYS; daysAhead += 1) {
    const future = resolveDay(snapshot, addLocalDays(date, daysAhead));
    if (future.isSchoolDay) {
      return occurrence(future.date, future.periods[0]);
    }
  }

  return null;
}

export function getStatus(snapshot: ScheduleSnapshot, now: Date): LessonStatus {
  const day = resolveDay(snapshot, now);
  const current = findCurrentPeriod(day, wallClockMinutes(now));
  const next = findNextPeriod(snapshot, now);
  return buildLessonStatus(now, day, current, next);
}

export function buildLessonStatus(
  timestamp: Date,
  day: EffectiveDay,
  current: PeriodOccurrence | null,
  next: PeriodOccurrence | null,
): LessonStatus {
  if (!current) {
    return {
      timestamp,
      day,
      current: null,
      next,
      timeRemainingMs: null,
      progress: null,
    };
  }

  const start = parseTimeToMinutes(current.period.startTime);
  const end = parseTimeToMinutes(current.period.endTime);
  const at = wallClockMinutes(timestamp);
  return {
    timestamp,
    day,
    current,
    next,
    timeRemainingMs: Math.max(0, (end - at) * 60_000),
    progress: clamp((at - start) / (end - start), 0, 1),
  };
}

export function getNotificationEvents(
  snapshot: ScheduleSnapshot,
  date: Date,
  endWarningLeadMinutes: number,
): NotificationEvent[] {
  if (endWarningLeadMinutes < 0) {
    throw new RangeError('End-warning lead cannot be negative.');
  }

  const events: NotificationEvent[] = [];
  const day = resolveDay(snapshot, date);
  for (const period of day.periods) {
    const item = occurrence(day.date, period);
    events.push({
      key: eventKey('start', period.id, day.date),
      kind: 'start',
      occurrence: item,
      triggerTime: item.startsAt,
    });

    const duration = parseTimeToMinutes(period.endTime) - parseTimeToMinutes(period.startTime);
    if (endWarningLeadMinutes > 0 && duration > endWarningLeadMinutes) {
      events.push({
        key: eventKey('end-warning', period.id, day.date),
        kind: 'end-warning',
        occurrence: item,
        triggerTime: localDateAtMinutes(
          day.date,
          parseTimeToMinutes(period.endTime) - endWarningLeadMinutes,
        ),
      });
    }
  }

  return events.sort(
    (left, right) =>
      left.triggerTime.getTime() - right.triggerTime.getTime() ||
      ordinalCompare(left.key, right.key),
  );
}

export function formatLocalDate(date: Date): string {
  return `${date.getFullYear().toString().padStart(4, '0')}-${(date.getMonth() + 1)
    .toString()
    .padStart(2, '0')}-${date.getDate().toString().padStart(2, '0')}`;
}

function eventKey(kind: string, periodId: string, date: Date): string {
  const id = periodId.replaceAll('-', '').toLowerCase();
  return `${kind}:${id}:${formatLocalDate(date)}`;
}

function isValidPeriod(period: Period): boolean {
  return parseTimeToMinutes(period.endTime) > parseTimeToMinutes(period.startTime);
}

function occurrence(date: Date, period: Period): PeriodOccurrence {
  return {
    date: startOfLocalDay(date),
    period,
    startsAt: localDateAtMinutes(date, parseTimeToMinutes(period.startTime)),
    endsAt: localDateAtMinutes(date, parseTimeToMinutes(period.endTime)),
  };
}

function startOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addLocalDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

function localDateAtMinutes(date: Date, minutes: number): Date {
  return new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate(),
    Math.floor(minutes / 60),
    minutes % 60,
  );
}

function wallClockMinutes(date: Date): number {
  return date.getHours() * 60 + date.getMinutes() + date.getSeconds() / 60 + date.getMilliseconds() / 60_000;
}

function findLast<T>(values: readonly T[], predicate: (value: T) => boolean): T | undefined {
  for (let index = values.length - 1; index >= 0; index -= 1) {
    if (predicate(values[index])) {
      return values[index];
    }
  }
  return undefined;
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}
