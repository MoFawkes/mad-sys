import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';

import { NOTIFICATION_CHANNEL_ID } from './planner';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldPlaySound: true,
    shouldSetBadge: false,
    shouldShowBanner: true,
    shouldShowList: true,
  }),
});

export async function initializeNotificationsAsync(): Promise<boolean> {
  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync(NOTIFICATION_CHANNEL_ID, {
      name: 'Lessons',
      description: 'Lesson starts and end warnings',
      importance: Notifications.AndroidImportance.HIGH,
      sound: 'default',
    });
  }

  let permissions = await Notifications.getPermissionsAsync();
  if (!permissionAllowsNotifications(permissions)) {
    permissions = await Notifications.requestPermissionsAsync();
  }
  return permissionAllowsNotifications(permissions);
}

export function permissionAllowsNotifications(
  permissions: Notifications.NotificationPermissionsStatus,
): boolean {
  if (permissions.granted) return true;
  const iosStatus = permissions.ios?.status;
  return (
    iosStatus === Notifications.IosAuthorizationStatus.AUTHORIZED ||
    iosStatus === Notifications.IosAuthorizationStatus.PROVISIONAL ||
    iosStatus === Notifications.IosAuthorizationStatus.EPHEMERAL
  );
}
