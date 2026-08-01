import type { ScheduleSnapshot } from './scheduleTypes';

export type DeviceAudienceRole = 'Teacher' | 'Admin' | 'StudentDevice';
export type SessionHalfDay = 'am' | 'pm';
export type AnnouncementAudience =
  | 'everyone'
  | 'teachers'
  | 'graduates'
  | 'am'
  | 'pm'
  | 'specific_class';

export type DeviceAudience = {
  role: DeviceAudienceRole;
  selectedClassIds: ReadonlySet<string>;
  optedHalfDays: ReadonlySet<SessionHalfDay>;
};

export type AudienceAnnouncement = {
  audienceType: AnnouncementAudience;
  audienceClassId?: string | null;
};

export function matchesAnnouncement(
  audience: DeviceAudience,
  announcement: AudienceAnnouncement,
): boolean {
  switch (announcement.audienceType) {
    case 'everyone':
      return true;
    case 'teachers':
      return audience.role === 'Teacher' || audience.role === 'Admin';
    case 'graduates':
      return false;
    case 'am':
    case 'pm':
      return audience.optedHalfDays.has(announcement.audienceType);
    case 'specific_class':
      return (
        announcement.audienceClassId != null &&
        audience.selectedClassIds.has(announcement.audienceClassId)
      );
    default:
      return false;
  }
}

export function matchesPeriod(
  audience: DeviceAudience,
  periodClassIds: ReadonlySet<string>,
): boolean {
  if (audience.role !== 'StudentDevice' || periodClassIds.size === 0) {
    return true;
  }

  for (const id of periodClassIds) {
    if (audience.selectedClassIds.has(id)) {
      return true;
    }
  }
  return false;
}

export function filterScheduleForAudience(
  snapshot: ScheduleSnapshot,
  audience: DeviceAudience,
): ScheduleSnapshot {
  if (audience.role !== 'StudentDevice') return snapshot;

  return {
    ...snapshot,
    timetables: snapshot.timetables.map((timetable) => ({
      ...timetable,
      periods: timetable.periods.filter((period) =>
        matchesPeriod(audience, new Set(period.classIds ?? [])),
      ),
    })),
  };
}
