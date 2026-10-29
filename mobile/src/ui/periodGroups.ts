import { ScheduledPeriod } from '@/src/domain/scheduleTypes';

export type PeriodGroup = {
  key: string;
  periods: ScheduledPeriod[];
  sharedName: string | null;
};

function seconds(time: string): number {
  const [hours, minutes, seconds = '0'] = time.split(':');
  return Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds);
}

export function groupPeriods(periods: readonly ScheduledPeriod[], classOrder: ReadonlyMap<string, number> = new Map()): PeriodGroup[] {
  const groups = new Map<string, ScheduledPeriod[]>();
  for (const entry of periods) {
    const key = `${seconds(entry.period.startTime)}:${seconds(entry.period.endTime)}`;
    const group = groups.get(key);
    if (group) group.push(entry);
    else groups.set(key, [entry]);
  }
  return [...groups].map(([key, entries]) => ({
    key,
    periods: [...entries].sort((left, right) => {
      const order = (classOrder.get(left.classId ?? '') ?? Number.MAX_SAFE_INTEGER)
        - (classOrder.get(right.classId ?? '') ?? Number.MAX_SAFE_INTEGER);
      if (order !== 0) return order;
      const leftId = left.classId ?? '';
      const rightId = right.classId ?? '';
      return leftId < rightId ? -1 : leftId > rightId ? 1 : 0;
    }),
    sharedName: entries.length > 1 && entries.every(({ period }) =>
      period.name.toLowerCase() === entries[0].period.name.toLowerCase()) ? entries[0].period.name : null,
  }));
}
