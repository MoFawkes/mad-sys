import * as SQLite from 'expo-sqlite';

import { applyCacheMigrations } from './migrations';

export const SYNC_TABLES = [
  'organizations',
  'timetables',
  'periods',
  'week_schedule',
  'date_overrides',
  'announcements',
  'profiles',
  'classes',
  'period_classes',
] as const;

export type SyncTable = (typeof SYNC_TABLES)[number];
export type RemoteRow = Record<string, unknown>;

let databasePromise: Promise<SQLite.SQLiteDatabase> | null = null;

export async function getDatabase(): Promise<SQLite.SQLiteDatabase> {
  if (!databasePromise) {
    databasePromise = SQLite.openDatabaseAsync('aqiclock.db').then(async (database) => {
      await applyCacheMigrations(database);
      return database;
    });
  }
  return databasePromise;
}

type InsertSpec = {
  sql: string;
  values(row: RemoteRow): SQLite.SQLiteBindParams;
};

const INSERTS: Record<SyncTable, InsertSpec> = {
  organizations: {
    sql: 'INSERT INTO organizations(id,name,timezone) VALUES (?, ?, ?)',
    values: (row) => [text(row.id), text(row.name), text(row.timezone)],
  },
  profiles: {
    sql: 'INSERT INTO profiles(id,display_name,role,is_active) VALUES (?, ?, ?, ?)',
    values: (row) => [text(row.id), text(row.display_name), text(row.role), bool(row.is_active)],
  },
  timetables: {
    sql: 'INSERT INTO timetables(id,name,is_archived) VALUES (?, ?, ?)',
    values: (row) => [text(row.id), text(row.name), bool(row.is_archived)],
  },
  periods: {
    sql: 'INSERT INTO periods(id,timetable_id,name,start_time,end_time,sort_order,is_lesson) VALUES (?, ?, ?, ?, ?, ?, ?)',
    values: (row) => [
      text(row.id),
      text(row.timetable_id),
      text(row.name),
      text(row.start_time),
      text(row.end_time),
      number(row.sort_order),
      bool(row.is_lesson),
    ],
  },
  classes: {
    sql: 'INSERT INTO classes(id,name,sort_order) VALUES (?, ?, ?)',
    values: (row) => [text(row.id), text(row.name), number(row.sort_order)],
  },
  period_classes: {
    sql: 'INSERT INTO period_classes(period_id,class_id) VALUES (?, ?)',
    values: (row) => [text(row.period_id), text(row.class_id)],
  },
  week_schedule: {
    sql: 'INSERT INTO week_schedule(id,weekday,audience_class_id,timetable_id) VALUES (?, ?, ?, ?)',
    values: (row) => [text(row.id), number(row.weekday), nullableText(row.audience_class_id), nullableText(row.timetable_id)],
  },
  date_overrides: {
    sql: 'INSERT INTO date_overrides(id,date,timetable_id,note) VALUES (?, ?, ?, ?)',
    values: (row) => [
      text(row.id),
      text(row.date),
      nullableText(row.timetable_id),
      nullableText(row.note),
    ],
  },
  announcements: {
    sql: `INSERT INTO announcements(
      id,title,body,expires_at,created_by,created_at,audience_type,audience_class_id,
      update_type,publish_at,e_masjid_link,status,deleted_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    values: (row) => [
      text(row.id),
      text(row.title),
      text(row.body),
      nullableText(row.expires_at),
      text(row.created_by),
      text(row.created_at),
      text(row.audience_type ?? 'everyone'),
      nullableText(row.audience_class_id),
      text(row.update_type ?? 'general'),
      nullableText(row.publish_at),
      nullableText(row.e_masjid_link),
      text(row.status ?? 'published'),
      nullableText(row.deleted_at),
    ],
  },
};

export async function replaceSnapshot(
  table: SyncTable,
  rows: readonly RemoteRow[],
  syncedAt = new Date(),
): Promise<void> {
  const database = await getDatabase();
  await replaceSnapshotInDatabase(database, table, rows, syncedAt);
}

export async function replaceSnapshotInDatabase(
  database: SQLite.SQLiteDatabase,
  table: SyncTable,
  rows: readonly RemoteRow[],
  syncedAt = new Date(),
): Promise<void> {
  const insert = INSERTS[table];
  await database.withExclusiveTransactionAsync(async (transaction) => {
    await transaction.runAsync(`DELETE FROM ${table}`);
    for (const row of rows) {
      await transaction.runAsync(insert.sql, insert.values(row));
    }
    await transaction.runAsync(
      `INSERT INTO sync_state(table_name,last_synced_at) VALUES(?,?)
       ON CONFLICT(table_name) DO UPDATE SET last_synced_at=excluded.last_synced_at`,
      [table, syncedAt.toISOString()],
    );
  });
}

export async function getMeta(key: string): Promise<string | null> {
  const database = await getDatabase();
  const row = await database.getFirstAsync<{ value: string }>(
    'SELECT value FROM meta WHERE key=?',
    key,
  );
  return row?.value ?? null;
}

export async function setMeta(key: string, value: string): Promise<void> {
  const database = await getDatabase();
  await database.runAsync(
    `INSERT INTO meta(key,value) VALUES(?,?)
     ON CONFLICT(key) DO UPDATE SET value=excluded.value`,
    [key, value],
  );
}

export async function wipeCache(): Promise<void> {
  const database = await getDatabase();
  await database.withExclusiveTransactionAsync(async (transaction) => {
    for (const table of SYNC_TABLES) {
      await transaction.runAsync(`DELETE FROM ${table}`);
    }
    await transaction.runAsync('DELETE FROM sync_state');
    await transaction.runAsync('DELETE FROM notification_log');
    await transaction.runAsync('DELETE FROM announcement_read');
    await transaction.runAsync('DELETE FROM student_preferences');
    await transaction.runAsync('DELETE FROM meta');
  });
}

function text(value: unknown): string {
  if (typeof value !== 'string') throw new TypeError('Expected text database value.');
  return value;
}

function nullableText(value: unknown): string | null {
  return value == null ? null : text(value);
}

function number(value: unknown): number {
  if (typeof value !== 'number') throw new TypeError('Expected numeric database value.');
  return value;
}

function bool(value: unknown): number {
  if (typeof value !== 'boolean') throw new TypeError('Expected boolean database value.');
  return value ? 1 : 0;
}
