import type { SQLiteDatabase } from 'expo-sqlite';

export type CacheMigration = {
  version: number;
  sql: string;
};

export const CACHE_MIGRATIONS: readonly CacheMigration[] = [
  {
    version: 1,
    sql: `
CREATE TABLE IF NOT EXISTS profiles (id TEXT PRIMARY KEY, display_name TEXT NOT NULL, role TEXT NOT NULL, is_active INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS timetables (id TEXT PRIMARY KEY, name TEXT NOT NULL, is_archived INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS periods (id TEXT PRIMARY KEY, timetable_id TEXT NOT NULL, name TEXT NOT NULL, start_time TEXT NOT NULL, end_time TEXT NOT NULL, sort_order INTEGER NOT NULL, is_lesson INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_periods_timetable_id ON periods(timetable_id);
CREATE TABLE IF NOT EXISTS week_schedule (weekday INTEGER PRIMARY KEY, timetable_id TEXT NULL);
CREATE TABLE IF NOT EXISTS date_overrides (id TEXT PRIMARY KEY, date TEXT NOT NULL, timetable_id TEXT NULL, note TEXT NULL);
CREATE INDEX IF NOT EXISTS ix_date_overrides_date ON date_overrides(date);
CREATE TABLE IF NOT EXISTS announcements (
  id TEXT PRIMARY KEY, title TEXT NOT NULL, body TEXT NOT NULL, expires_at TEXT NULL,
  created_by TEXT NOT NULL, created_at TEXT NOT NULL,
  audience_type TEXT NOT NULL DEFAULT 'everyone', audience_class_id TEXT NULL,
  update_type TEXT NOT NULL DEFAULT 'general', publish_at TEXT NULL,
  e_masjid_link TEXT NULL, status TEXT NOT NULL DEFAULT 'published', deleted_at TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_announcements_expires_at ON announcements(expires_at);
CREATE INDEX IF NOT EXISTS ix_announcements_publish_status ON announcements(status, publish_at, deleted_at);
CREATE TABLE IF NOT EXISTS classes (id TEXT PRIMARY KEY, name TEXT NOT NULL, sort_order INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS period_classes (
  period_id TEXT NOT NULL, class_id TEXT NOT NULL, PRIMARY KEY (period_id, class_id)
);
CREATE INDEX IF NOT EXISTS ix_period_classes_class_id ON period_classes(class_id);
CREATE TABLE IF NOT EXISTS sync_state (table_name TEXT PRIMARY KEY, last_synced_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS notification_log (event_key TEXT PRIMARY KEY, fired_at TEXT NULL, skipped INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS announcement_read (announcement_id TEXT PRIMARY KEY, read_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
`,
  },
  {
    version: 2,
    sql: `
CREATE TABLE student_preferences (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  selected_class_ids TEXT NOT NULL,
  opted_am INTEGER NOT NULL DEFAULT 0,
  opted_pm INTEGER NOT NULL DEFAULT 0
);
`,
  },
  {
    version: 3,
    sql: `
DROP TABLE week_schedule;
CREATE TABLE week_schedule (id TEXT PRIMARY KEY, weekday INTEGER NOT NULL, audience_class_id TEXT NULL, timetable_id TEXT NULL);
CREATE INDEX ix_week_schedule_weekday ON week_schedule(weekday);
`,
  },
  {
    version: 4,
    sql: `
CREATE TABLE organizations (id TEXT PRIMARY KEY, name TEXT NOT NULL, timezone TEXT NOT NULL);
`,
  },
  {
    version: 5,
    sql: `
CREATE TABLE notification_delivery (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  event_key TEXT, identifier TEXT, delivered_at TEXT NOT NULL,
  observed_via TEXT NOT NULL, title TEXT, body TEXT, trigger_time TEXT
);
CREATE UNIQUE INDEX ux_notification_delivery_identifier_delivered_at
  ON notification_delivery(identifier, delivered_at);
CREATE TABLE notification_schedule_snapshot (
  id INTEGER PRIMARY KEY AUTOINCREMENT, captured_at TEXT NOT NULL, payload TEXT NOT NULL
);
`,
  },
  {
    version: 6,
    sql: `
DROP INDEX ux_notification_delivery_identifier_delivered_at;
CREATE UNIQUE INDEX ux_notification_delivery_identifier_delivered_at
  ON notification_delivery(COALESCE(identifier, ''), delivered_at);
`,
  },
];

export async function applyCacheMigrations(database: SQLiteDatabase): Promise<void> {
  await database.execAsync('PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;');
  const row = await database.getFirstAsync<{ user_version: number }>('PRAGMA user_version');
  const currentVersion = row?.user_version ?? 0;
  const pending = CACHE_MIGRATIONS.filter((migration) => migration.version > currentVersion);

  for (const migration of pending) {
    await database.withExclusiveTransactionAsync(async (transaction) => {
      await transaction.execAsync(migration.sql);
      await transaction.execAsync(`PRAGMA user_version=${migration.version}`);
    });
  }
}
