import {
  exportNotificationDiagnostics,
  serializeNotificationDiagnostics,
} from '@/src/notifications/diagnostics';

const mockCreate = jest.fn();
const mockWrite = jest.fn();
jest.mock('expo-file-system', () => ({
  File: jest.fn(() => ({ create: mockCreate, write: mockWrite, uri: 'cache/diagnostics.json' })),
  Paths: { cache: 'cache' },
}));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));
jest.mock('@/src/data/repositories', () => ({
  getNotificationDeliveries: jest.fn(async () => []),
  getNotificationScheduleSnapshots: jest.fn(async () => []),
}));

describe('notification diagnostics export', () => {
  it('serializes delivery rows and schedule snapshots as readable JSON', () => {
    const json = serializeNotificationDiagnostics({
      exportedAt: '2026-08-27T20:00:00.000Z',
      notificationDelivery: [{
        id: 1,
        eventKey: 'start:lesson:2026-08-27',
        identifier: 'start:lesson:2026-08-27',
        deliveredAt: '2026-08-27T18:40:00.000Z',
        observedVia: 'presented_sweep',
        title: 'Lesson starting',
        body: 'Lesson 1',
        triggerTime: '2026-08-27T18:40:00.000Z',
      }],
      notificationScheduleSnapshots: [{
        id: 2,
        capturedAt: '2026-08-27T17:00:00.000Z',
        payload: '{"desired":[],"actual":[]}',
      }],
    });

    const parsed = JSON.parse(json) as Record<string, unknown>;
    expect(parsed.exportedAt).toBe('2026-08-27T20:00:00.000Z');
    expect(parsed.notificationDelivery).toHaveLength(1);
    expect(parsed.notificationScheduleSnapshots).toHaveLength(1);
    expect(json).toContain('\n  "notificationDelivery"');
  });

  it('writes a JSON file and opens the native share sheet', async () => {
    const Sharing = jest.requireMock('expo-sharing') as {
      isAvailableAsync: jest.Mock;
      shareAsync: jest.Mock;
    };
    Sharing.isAvailableAsync.mockResolvedValue(true);
    Sharing.shareAsync.mockResolvedValue(undefined);

    await exportNotificationDiagnostics(new Date('2026-08-27T20:00:00.000Z'));

    expect(mockCreate).toHaveBeenCalledWith({ overwrite: true });
    expect(mockWrite).toHaveBeenCalledWith(expect.stringContaining('"exportedAt"'));
    expect(Sharing.shareAsync).toHaveBeenCalledWith(
      'cache/diagnostics.json',
      expect.objectContaining({ mimeType: 'application/json' }),
    );
  });
});
