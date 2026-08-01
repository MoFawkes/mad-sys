import type { SQLiteDatabase } from 'expo-sqlite';

import { applyCacheMigrations, CACHE_MIGRATIONS } from '@/src/data/migrations';
import { replaceSnapshotInDatabase } from '@/src/data/sqlite';

jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));

describe('SQLite cache safety', () => {
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
});
