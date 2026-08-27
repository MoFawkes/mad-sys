import { openDatabaseAsync, type SQLiteDatabase } from 'expo-sqlite';

import { applyCacheMigrations, CACHE_MIGRATIONS } from '@/src/data/migrations';
import { replaceSnapshotInDatabase, SYNC_TABLES, wipeCache } from '@/src/data/sqlite';

jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));

describe('SQLite cache safety', () => {
  it('adds durable delivery and schedule evidence in migration 5', () => {
    const migration = CACHE_MIGRATIONS.find((item) => item.version === 5);
    expect(migration?.sql).toContain('CREATE TABLE notification_delivery');
    expect(migration?.sql).toContain('CREATE TABLE notification_schedule_snapshot');
  });

  it('applies ordered migrations once using user_version', async () => {
    let version = 0;
    let migrationExecutions = 0;
    const database = {
      execAsync: async () => {},
      getFirstAsync: async () => ({ user_version: version }),
      withExclusiveTransactionAsync: async (
        task: (transaction: SQLiteDatabase) => Promise<void>,
      ) => {
        const transaction = {
          execAsync: async (sql: string) => {
            if (sql.startsWith('PRAGMA user_version=')) {
              version = Number(sql.split('=')[1]);
            } else {
              migrationExecutions += 1;
            }
          },
        } as unknown as SQLiteDatabase;
        await task(transaction);
      },
    } as unknown as SQLiteDatabase;

    await applyCacheMigrations(database);
    await applyCacheMigrations(database);

    expect(version).toBe(CACHE_MIGRATIONS.at(-1)?.version);
    expect(migrationExecutions).toBe(CACHE_MIGRATIONS.length);
  });

  it('rolls back the delete when inserting a malformed snapshot row fails', async () => {
    let committedRows = [{ id: 'old' }];
    const database = {
      withExclusiveTransactionAsync: async (
        task: (transaction: SQLiteDatabase) => Promise<void>,
      ) => {
        let transactionRows = [...committedRows];
        const transaction = {
          runAsync: async (sql: string, values?: unknown) => {
            if (sql.startsWith('DELETE')) transactionRows = [];
            if (sql.startsWith('INSERT INTO timetables')) {
              transactionRows.push({ id: (values as string[])[0] });
            }
          },
        } as unknown as SQLiteDatabase;
        await task(transaction);
        committedRows = transactionRows;
      },
    } as unknown as SQLiteDatabase;

    await expect(
      replaceSnapshotInDatabase(database, 'timetables', [
        { id: 'new', is_archived: false },
      ]),
    ).rejects.toThrow('Expected text database value.');
    expect(committedRows).toEqual([{ id: 'old' }]);
  });

  it('wipes every cached data, session, preference, and notification table atomically', async () => {
    const executedSql: string[] = [];
    const transaction = {
      runAsync: jest.fn(async (sql: string) => {
        executedSql.push(sql);
      }),
    } as unknown as SQLiteDatabase;
    const database = {
      execAsync: jest.fn(async () => {}),
      getFirstAsync: jest.fn(async () => ({ user_version: CACHE_MIGRATIONS.at(-1)?.version })),
      withExclusiveTransactionAsync: jest.fn(
        async (task: (transaction: SQLiteDatabase) => Promise<void>) => task(transaction),
      ),
    } as unknown as SQLiteDatabase;
    jest.mocked(openDatabaseAsync).mockResolvedValue(database);

    await wipeCache();

    expect(database.withExclusiveTransactionAsync).toHaveBeenCalledTimes(1);
    expect(executedSql).toEqual([
      ...SYNC_TABLES.map((table) => `DELETE FROM ${table}`),
      'DELETE FROM sync_state',
      'DELETE FROM notification_log',
      'DELETE FROM notification_delivery',
      'DELETE FROM notification_schedule_snapshot',
      'DELETE FROM announcement_read',
      'DELETE FROM student_preferences',
      'DELETE FROM meta',
    ]);
  });
});
