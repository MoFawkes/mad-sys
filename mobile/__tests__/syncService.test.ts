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

  // Students previously pulled `profiles` too. RLS returns an empty snapshot for
  // an anonymous device, and that empty-but-successful result was read as "this
  // account is deactivated", flipping the session into teacher mode and showing
  // "Your account is inactive" instead of the timetable.
  it('omits profiles from the student snapshot entirely', () => {
    expect(syncOrderFor('student')).toEqual([
      'timetables',
      'periods',
      'week_schedule',
      'date_overrides',
      'announcements',
      'classes',
      'period_classes',
    ]);
    expect(syncOrderFor('student')).not.toContain('profiles');
  });
});
