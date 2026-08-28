import * as Notifications from 'expo-notifications';
import { AppState } from 'react-native';

import {
  NotificationDelivery,
  recordNotificationDelivery,
} from '@/src/data/repositories';

export function notificationToDelivery(
  notification: Notifications.Notification,
  observedVia: NotificationDelivery['observedVia'],
): NotificationDelivery {
  const { content, identifier } = notification.request;
  const eventKey = typeof content.data?.eventKey === 'string'
    ? content.data.eventKey
    : identifier || null;
  const triggerValue = content.data?.aqiTriggerTime;
  return {
    eventKey,
    identifier: identifier || null,
    deliveredAt: new Date(notification.date).toISOString(),
    observedVia,
    title: content.title ?? null,
    body: content.body ?? null,
    triggerTime: typeof triggerValue === 'number'
      ? new Date(triggerValue).toISOString()
      : typeof triggerValue === 'string'
        ? triggerValue
        : null,
  };
}

export async function sweepPresentedNotifications(): Promise<void> {
  const notifications = await Notifications.getPresentedNotificationsAsync();
  for (const notification of notifications) {
    await recordNotificationDelivery(notificationToDelivery(notification, 'presented_sweep'));
  }
}

export function registerNotificationDeliveryCapture(): () => void {
  const notificationSubscription = Notifications.addNotificationReceivedListener((notification) => {
    void recordNotificationDelivery(
      notificationToDelivery(notification, 'foreground_listener'),
    ).catch(() => undefined);
  });
  const appStateSubscription = AppState.addEventListener('change', (state) => {
    if (state === 'active') void sweepPresentedNotifications().catch(() => undefined);
  });
  void sweepPresentedNotifications().catch(() => undefined);

  return () => {
    notificationSubscription.remove();
    appStateSubscription.remove();
  };
}
