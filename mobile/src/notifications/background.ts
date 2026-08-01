import * as BackgroundTask from 'expo-background-task';
import * as TaskManager from 'expo-task-manager';

import { reconcileScheduledNotificationsFromCache } from './planner';

export const NOTIFICATION_BACKGROUND_TASK = 'aqi-clock-notification-reconcile';

if (!TaskManager.isTaskDefined(NOTIFICATION_BACKGROUND_TASK)) {
  TaskManager.defineTask(NOTIFICATION_BACKGROUND_TASK, async () => {
    try {
      await reconcileScheduledNotificationsFromCache();
      return BackgroundTask.BackgroundTaskResult.Success;
    } catch {
      return BackgroundTask.BackgroundTaskResult.Failed;
    }
  });
}

export async function registerNotificationBackgroundTaskAsync(): Promise<void> {
  const available = await BackgroundTask.getStatusAsync();
  if (available !== BackgroundTask.BackgroundTaskStatus.Available) return;
  if (await TaskManager.isTaskRegisteredAsync(NOTIFICATION_BACKGROUND_TASK)) return;
  await BackgroundTask.registerTaskAsync(NOTIFICATION_BACKGROUND_TASK, {
    minimumInterval: 6 * 60,
  });
}
