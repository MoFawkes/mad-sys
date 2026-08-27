import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';

import {
  getNotificationDeliveries,
  getNotificationScheduleSnapshots,
  NotificationDelivery,
  NotificationScheduleSnapshot,
} from '@/src/data/repositories';

type DiagnosticsPayload = {
  exportedAt: string;
  notificationDelivery: (NotificationDelivery & { id: number })[];
  notificationScheduleSnapshots: NotificationScheduleSnapshot[];
};

export function serializeNotificationDiagnostics(payload: DiagnosticsPayload): string {
  return JSON.stringify(payload, null, 2);
}

export async function exportNotificationDiagnostics(now = new Date()): Promise<void> {
  if (!(await Sharing.isAvailableAsync())) {
    throw new Error('File sharing is not available on this device.');
  }
  const payload: DiagnosticsPayload = {
    exportedAt: now.toISOString(),
    notificationDelivery: await getNotificationDeliveries(),
    notificationScheduleSnapshots: await getNotificationScheduleSnapshots(),
  };
  const timestamp = now.toISOString().replace(/[:.]/g, '-');
  const file = new File(Paths.cache, `aqi-clock-notification-diagnostics-${timestamp}.json`);
  file.create({ overwrite: true });
  file.write(serializeNotificationDiagnostics(payload));
  await Sharing.shareAsync(file.uri, {
    dialogTitle: 'Export notification diagnostics',
    mimeType: 'application/json',
    UTI: 'public.json',
  });
}
