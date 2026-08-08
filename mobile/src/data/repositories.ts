import {
  Announcement,
  AnnouncementAudience,
  AnnouncementUpdateType,
  DateOverride,
  DeviceAudience,
  isAnnouncementVisible,
  Period,
  ScheduleSnapshot,
  Timetable,
  WeekSchedule,
} from '@/src/domain';

import { getDatabase, SyncTable } from './sqlite';

type TimetableRow = { id: string; name: string; is_archived: number };
type PeriodRow = {
  id: string;
  timetable_id: string;
  name: string;
  start_time: string;
  end_time: string;
  sort_order: number;
  is_lesson: number;
};

export type CachedProfile = {
  id: string;
  displayName: string;
  role: 'teacher' | 'admin' | 'graduate';
  isActive: boolean;
};

export type CachedClass = {
  id: string;
  name: string;
  sortOrder: number;
};

export type StudentPreferences = {
  selectedClassIds: string[];
  optedAm: boolean;
  optedPm: boolean;
};

export async function getClasses(): Promise<CachedClass[]> {
  const database = await getDatabase();
  const rows = await database.getAllAsync<{
    id: string;
    name: string;
    sort_order: number;
  }>('SELECT id,name,sort_order FROM classes ORDER BY sort_order,name');
  return rows.map((row) => ({
    id: row.id,
    name: row.name,
    sortOrder: row.sort_order,
  }));
}

export async function getStudentPreferences(): Promise<StudentPreferences | null> {
  const database = await getDatabase();
  const row = await database.getFirstAsync<{
    selected_class_ids: string;
    opted_am: number;
    opted_pm: number;
  }>('SELECT selected_class_ids,opted_am,opted_pm FROM student_preferences WHERE id=1');
  if (!row) return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(row.selected_class_ids);
  } catch {
    return null;
  }
  if (!Array.isArray(parsed) || !parsed.every((id) => typeof id === 'string')) {
    return null;
  }
  return {
    selectedClassIds: parsed,
    optedAm: row.opted_am !== 0,
    optedPm: row.opted_pm !== 0,
  };
}

export async function saveStudentPreferences(
  preferences: StudentPreferences,
): Promise<void> {
  if (preferences.selectedClassIds.length === 0) {
    throw new Error('Select at least one class.');
  }
  const database = await getDatabase();
  await database.runAsync(
    `INSERT INTO student_preferences(id,selected_class_ids,opted_am,opted_pm)
     VALUES(1,?,?,?)
     ON CONFLICT(id) DO UPDATE SET
       selected_class_ids=excluded.selected_class_ids,
       opted_am=excluded.opted_am,
       opted_pm=excluded.opted_pm`,
    [
      JSON.stringify([...new Set(preferences.selectedClassIds)]),
      preferences.optedAm ? 1 : 0,
      preferences.optedPm ? 1 : 0,
    ],
  );
}

export async function loadScheduleSnapshot(): Promise<ScheduleSnapshot> {
  const database = await getDatabase();
  const [timetableRows, periodRows, weekRows, overrideRows, periodClassRows] = await Promise.all([
    database.getAllAsync<TimetableRow>('SELECT id,name,is_archived FROM timetables ORDER BY name'),
    database.getAllAsync<PeriodRow>(
      'SELECT id,timetable_id,name,start_time,end_time,sort_order,is_lesson FROM periods ORDER BY sort_order',
    ),
    database.getAllAsync<{ id: string; weekday: number; audience_class_id: string | null; timetable_id: string | null }>(
      'SELECT id,weekday,audience_class_id,timetable_id FROM week_schedule',
    ),
    database.getAllAsync<{
      id: string;
      date: string;
      timetable_id: string | null;
      note: string | null;
    }>('SELECT id,date,timetable_id,note FROM date_overrides ORDER BY date'),
    database.getAllAsync<{ period_id: string; class_id: string }>(
      'SELECT period_id,class_id FROM period_classes',
    ),
  ]);

  const classIdsByPeriod = new Map<string, string[]>();
  for (const row of periodClassRows) {
    const ids = classIdsByPeriod.get(row.period_id) ?? [];
    ids.push(row.class_id);
    classIdsByPeriod.set(row.period_id, ids);
  }

  const periodsByTimetable = new Map<string, Period[]>();
  for (const row of periodRows) {
    const periods = periodsByTimetable.get(row.timetable_id) ?? [];
    periods.push({
      id: row.id,
      name: row.name,
      startTime: row.start_time,
      endTime: row.end_time,
      sortOrder: row.sort_order,
      isLesson: row.is_lesson !== 0,
      classIds: classIdsByPeriod.get(row.id) ?? [],
    });
    periodsByTimetable.set(row.timetable_id, periods);
  }

  const timetables: Timetable[] = timetableRows.map((row) => ({
    id: row.id,
    name: row.name,
    isArchived: row.is_archived !== 0,
    periods: periodsByTimetable.get(row.id) ?? [],
  }));
  const weekSchedule: WeekSchedule = weekRows.map((row) => ({ id: row.id, weekday: row.weekday, audienceClassId: row.audience_class_id, timetableId: row.timetable_id }));
  const dateOverrides: DateOverride[] = overrideRows.map((row) => ({
    id: row.id,
    date: row.date,
    timetableId: row.timetable_id,
    note: row.note,
  }));
  return { timetables, weekSchedule, dateOverrides };
}

export async function getCachedProfile(userId: string): Promise<CachedProfile | null> {
  const database = await getDatabase();
  const row = await database.getFirstAsync<{
    id: string;
    display_name: string;
    role: CachedProfile['role'];
    is_active: number;
  }>('SELECT id,display_name,role,is_active FROM profiles WHERE id=?', userId);
  return row
    ? {
        id: row.id,
        displayName: row.display_name,
        role: row.role,
        isActive: row.is_active !== 0,
      }
    : null;
}

export async function getLastSyncedAt(table?: SyncTable): Promise<Date | null> {
  const database = await getDatabase();
  const row = table
    ? await database.getFirstAsync<{ last_synced_at: string }>(
        'SELECT last_synced_at FROM sync_state WHERE table_name=?',
        table,
      )
    : await database.getFirstAsync<{ last_synced_at: string }>(
        'SELECT MIN(last_synced_at) AS last_synced_at FROM sync_state',
      );
  return row?.last_synced_at ? new Date(row.last_synced_at) : null;
}

type AnnouncementRow = {
  id: string;
  title: string;
  body: string;
  expires_at: string | null;
  created_by: string;
  created_at: string;
  audience_type: AnnouncementAudience;
  audience_class_id: string | null;
  update_type: AnnouncementUpdateType;
  publish_at: string | null;
  e_masjid_link: string | null;
  status: string;
  deleted_at: string | null;
  read_at: string | null;
};

export async function getVisibleAnnouncements(
  audience: DeviceAudience,
  now = new Date(),
): Promise<Announcement[]> {
  const database = await getDatabase();
  const rows = await database.getAllAsync<AnnouncementRow>(
    `SELECT a.id,a.title,a.body,a.expires_at,a.created_by,a.created_at,
            a.audience_type,a.audience_class_id,a.update_type,a.publish_at,
            a.e_masjid_link,a.status,a.deleted_at,r.read_at
       FROM announcements a
       LEFT JOIN announcement_read r ON r.announcement_id=a.id
      ORDER BY COALESCE(a.publish_at,a.created_at) DESC`,
  );
  return rows.map(mapAnnouncement).filter((item) => isAnnouncementVisible(item, audience, now));
}

export async function markAnnouncementRead(
  announcementId: string,
  readAt = new Date(),
): Promise<void> {
  const database = await getDatabase();
  await database.runAsync(
    `INSERT INTO announcement_read(announcement_id,read_at) VALUES(?,?)
     ON CONFLICT(announcement_id) DO UPDATE SET read_at=excluded.read_at`,
    [announcementId, readAt.toISOString()],
  );
}

export async function hasNotificationLogEntry(eventKey: string): Promise<boolean> {
  const database = await getDatabase();
  const row = await database.getFirstAsync<{ found: number }>(
    'SELECT 1 AS found FROM notification_log WHERE event_key=?',
    eventKey,
  );
  return row != null;
}

export async function recordNotificationLogEntry(
  eventKey: string,
  skipped: boolean,
  firedAt = new Date(),
): Promise<void> {
  const database = await getDatabase();
  await database.runAsync(
    `INSERT INTO notification_log(event_key,fired_at,skipped) VALUES(?,?,?)
     ON CONFLICT(event_key) DO NOTHING`,
    [eventKey, firedAt.toISOString(), skipped ? 1 : 0],
  );
}

function mapAnnouncement(row: AnnouncementRow): Announcement {
  return {
    id: row.id,
    title: row.title,
    body: row.body,
    expiresAt: row.expires_at,
    createdBy: row.created_by,
    createdAt: row.created_at,
    audienceType: row.audience_type,
    audienceClassId: row.audience_class_id,
    updateType: row.update_type,
    publishAt: row.publish_at,
    eMasjidLink: row.e_masjid_link,
    status: row.status,
    deletedAt: row.deleted_at,
    isRead: row.read_at != null,
  };
}
