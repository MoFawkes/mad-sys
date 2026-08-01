import * as Notifications from 'expo-notifications';

import {
  getLastSyncedAt,
  getVisibleAnnouncements,
  hasNotificationLogEntry,
  recordNotificationLogEntry,
} from '@/src/data/repositories';
import { getMeta, setMeta } from '@/src/data/sqlite';
import { DeviceAudience } from '@/src/domain';

import { permissionAllowsNotifications } from './permissions';
import { getNotificationSettings } from './settings';

const ANNOUNCEMENT_BASELINE_KEY = 'announcement_notification_baseline';

export async function processAnnouncementNotifications(
  audience: DeviceAudience,
  now = new Date(),
): Promise<void> {
  const [settings, permissions, announcements, lastSyncedAt, baseline] = await Promise.all([
    getNotificationSettings(),
    Notifications.getPermissionsAsync(),
    getVisibleAnnouncements(audience, now),
    getLastSyncedAt('announcements'),
    getMeta(ANNOUNCEMENT_BASELINE_KEY),
  ]);
  // Do not establish an empty baseline before the first complete snapshot has arrived.
  if (!lastSyncedAt) return;

  if (baseline !== 'true') {
    for (const announcement of announcements) {
      await recordNotificationLogEntry(notificationKey(announcement.id), true, now);
    }
    await setMeta(ANNOUNCEMENT_BASELINE_KEY, 'true');
    return;
  }

  const canPresent =
    settings.announcementsEnabled && permissionAllowsNotifications(permissions);

  for (const announcement of announcements) {
    const key = notificationKey(announcement.id);
    if (await hasNotificationLogEntry(key)) continue;

    if (canPresent) {
      await Notifications.scheduleNotificationAsync({
        identifier: key,
        content: {
          title: announcement.title,
          body:
            announcement.body.length <= 100
              ? announcement.body
              : `${announcement.body.slice(0, 100)}…`,
          sound: true,
          data: { aqiClock: true, kind: 'announcement', announcementId: announcement.id },
        },
        trigger: null,
      });
    }
    await recordNotificationLogEntry(key, !canPresent, now);
  }
}

function notificationKey(id: string): string {
  return `announcement:${id.replaceAll('-', '').toLowerCase()}`;
}
