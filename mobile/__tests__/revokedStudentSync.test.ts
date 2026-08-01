import { wipeCache } from '@/src/data/sqlite';
import {
  STUDENT_DEVICE_REVOKED_MESSAGE,
  SyncService,
} from '@/src/data/syncService';

jest.mock('@/src/data/sessionStore', () => ({
  getSupabaseClient: () => ({
    channel: jest.fn(() => ({
      on: jest.fn(() => ({ subscribe: jest.fn(() => ({})) })),
    })),
    from: jest.fn(() => ({
      select: jest.fn(() => ({
        limit: jest.fn(async () => ({ data: [], error: null })),
      })),
    })),
    removeChannel: jest.fn(async () => undefined),
  }),
}));

jest.mock('@/src/data/repositories', () => ({
  getLastSyncedAt: jest.fn(async () => null),
}));

jest.mock('@/src/data/sqlite', () => ({
  SYNC_TABLES: [
    'timetables',
    'periods',
    'week_schedule',
    'date_overrides',
    'announcements',
    'profiles',
    'classes',
    'period_classes',
  ],
  getMeta: jest.fn(async () => 'true'),
  replaceSnapshot: jest.fn(),
  setMeta: jest.fn(),
  wipeCache: jest.fn(async () => undefined),
}));

describe('revoked student device sync', () => {
  it('wipes the cache and surfaces the enrolment transition', async () => {
    const service = new SyncService();
    let latestError: string | null = null;
    const listener = jest.fn((state: { error: string | null }) => {
      latestError = state.error;
    });
    service.subscribe(listener);

    await service.start('student-user', 'student');

    expect(latestError).toBe(STUDENT_DEVICE_REVOKED_MESSAGE);
    expect(wipeCache).toHaveBeenCalledTimes(1);
    const wipeOrder = jest.mocked(wipeCache).mock.invocationCallOrder[0];
    const revokedSignalOrder = listener.mock.invocationCallOrder.at(-1);
    expect(wipeOrder).toBeLessThan(revokedSignalOrder!);
    await service.stop();
  });
});
