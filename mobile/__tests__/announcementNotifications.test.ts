import * as Notifications from 'expo-notifications';

import {
  getLastSyncedAt,
  getVisibleAnnouncements,
  hasNotificationLogEntry,
  recordNotificationLogEntry,
} from '@/src/data/repositories';
import { getMeta, setMeta } from '@/src/data/sqlite';
import { processAnnouncementNotifications } from '@/src/notifications/announcements';

jest.mock('expo-notifications', () => ({
  getPermissionsAsync: jest.fn(),
  scheduleNotificationAsync: jest.fn(),
}));
jest.mock('@/src/data/repositories', () => ({
  getLastSyncedAt: jest.fn(),
  getVisibleAnnouncements: jest.fn(),
  hasNotificationLogEntry: jest.fn(),
  recordNotificationLogEntry: jest.fn(),
}));
jest.mock('@/src/data/sqlite', () => ({
  getMeta: jest.fn(),
  setMeta: jest.fn(),
}));
jest.mock('@/src/notifications/settings', () => ({
  getNotificationSettings: jest.fn(async () => ({
    lessonStartEnabled: true,
    endWarningEnabled: true,
    endWarningMinutes: 5,
    announcementsEnabled: true,
  })),
}));
jest.mock('@/src/notifications/permissions', () => ({
  permissionAllowsNotifications: jest.fn(() => true),
}));

const teacher = {
  role: 'Teacher' as const,
  selectedClassIds: new Set<string>(),
  optedHalfDays: new Set<'am' | 'pm'>(),
};
const announcement = {
  id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  title: 'Existing announcement',
  body: 'Body',
};

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(Notifications.getPermissionsAsync).mockResolvedValue({
    granted: true,
  } as Notifications.NotificationPermissionsStatus);
  jest.mocked(getVisibleAnnouncements).mockResolvedValue([announcement] as never);
  jest.mocked(hasNotificationLogEntry).mockResolvedValue(false);
});

it('does not establish an empty baseline while another table has synced first', async () => {
  let announcementsSyncedAt: Date | null = null;
  let baseline: string | null = null;
  jest.mocked(getLastSyncedAt).mockImplementation(async (table) =>
    table === 'announcements'
      ? announcementsSyncedAt
      : new Date('2026-07-28T09:00:00Z'),
  );
  jest.mocked(getMeta).mockImplementation(async () => baseline);
  jest.mocked(setMeta).mockImplementation(async (_key, value) => {
    baseline = value;
  });
  jest.mocked(getVisibleAnnouncements)
    .mockResolvedValueOnce([])
    .mockResolvedValueOnce([announcement] as never);

  // A profiles sync has committed, but announcements has not.
  await processAnnouncementNotifications(teacher);
  expect(setMeta).not.toHaveBeenCalled();

  // The announcements snapshot then commits and contains existing rows.
  announcementsSyncedAt = new Date('2026-07-28T09:01:00Z');
  await processAnnouncementNotifications(teacher);

  expect(getLastSyncedAt).toHaveBeenCalledWith('announcements');
  expect(recordNotificationLogEntry).toHaveBeenCalledWith(
    'announcement:aaaaaaaabbbbccccddddeeeeeeeeeeee',
    true,
    expect.any(Date),
  );
  expect(setMeta).toHaveBeenCalledWith('announcement_notification_baseline', 'true');
  expect(Notifications.scheduleNotificationAsync).not.toHaveBeenCalled();
});

it('records currently visible announcements as skipped on the first synced pass', async () => {
  jest.mocked(getLastSyncedAt).mockResolvedValue(new Date('2026-07-28T09:00:00Z'));
  jest.mocked(getMeta).mockResolvedValue(null);

  await processAnnouncementNotifications(teacher, new Date('2026-07-28T09:01:00Z'));

  expect(recordNotificationLogEntry).toHaveBeenCalledWith(
    'announcement:aaaaaaaabbbbccccddddeeeeeeeeeeee',
    true,
    new Date('2026-07-28T09:01:00Z'),
  );
  expect(setMeta).toHaveBeenCalledWith('announcement_notification_baseline', 'true');
  expect(Notifications.scheduleNotificationAsync).not.toHaveBeenCalled();
});

it('presents newly visible announcements after the baseline exists', async () => {
  jest.mocked(getLastSyncedAt).mockResolvedValue(new Date('2026-07-28T09:00:00Z'));
  jest.mocked(getMeta).mockResolvedValue('true');

  await processAnnouncementNotifications(teacher);

  expect(Notifications.scheduleNotificationAsync).toHaveBeenCalledWith(
    expect.objectContaining({
      identifier: 'announcement:aaaaaaaabbbbccccddddeeeeeeeeeeee',
      trigger: null,
    }),
  );
  expect(recordNotificationLogEntry).toHaveBeenCalledWith(
    'announcement:aaaaaaaabbbbccccddddeeeeeeeeeeee',
    false,
    expect.any(Date),
  );
});
