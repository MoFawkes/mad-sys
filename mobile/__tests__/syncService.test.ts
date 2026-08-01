import { syncOrderFor } from '@/src/data/syncService';
import { readStoreLastSyncedAt } from '@/src/data/syncState';

jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));

describe('sync store state', () => {
  it('derives displayed last-sync time from SQLite rather than the current clock', async () => {
    const conservativeMinimum = new Date('2026-07-20T09:00:00.000Z');
    await expect(
      readStoreLastSyncedAt(async () => conservativeMinimum),
    ).resolves.toEqual(conservativeMinimum);
  });
});

describe('sync audience ordering', () => {
  it('keeps teacher profile-first validation', () => {
    expect(syncOrderFor('teacher')[0]).toBe('profiles');
  });

  it('syncs a student snapshot without requiring a profile first', () => {
    expect(syncOrderFor('student')).toEqual([
      'timetables',
      'periods',
      'week_schedule',
      'date_overrides',
      'announcements',
      'profiles',
      'classes',
      'period_classes',
    ]);
    expect(syncOrderFor('student')).toContain('profiles');
    expect(syncOrderFor('student')[0]).not.toBe('profiles');
  });
});
