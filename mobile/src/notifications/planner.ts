import * as Notifications from 'expo-notifications';

import {
  getCachedProfile,
  getStudentPreferences,
  loadScheduleSnapshot,
} from '@/src/data/repositories';
import { getSupabaseClient } from '@/src/data/sessionStore';
import {
  DeviceAudience,
  getNotificationEvents,
  matchesPeriod,
  ScheduleSnapshot,
} from '@/src/domain';

import { getNotificationSettings, NotificationSettings } from './settings';

export const NOTIFICATION_CHANNEL_ID = 'lessons';
export const NOTIFICATION_LIMIT = 60;
export const HORIZON_DAYS = 7;

export type DesiredNotification = {
  identifier: string;
  triggerTime: Date;
  title: string;
  body: string;
  kind: 'start' | 'end-warning';
};

export type ActualNotification = {
  identifier: string;
  triggerTimeMs: number | null;
};

export type ReconcileDiff = {
  cancel: string[];
  schedule: DesiredNotification[];
};

export function buildDesiredNotifications(
  snapshot: ScheduleSnapshot,
  audience: DeviceAudience,
  settings: NotificationSettings,
  now = new Date(),
): DesiredNotification[] {
  const desired: DesiredNotification[] = [];
  const firstDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());

  for (let offset = 0; offset < HORIZON_DAYS; offset += 1) {
    const date = new Date(
      firstDate.getFullYear(),
      firstDate.getMonth(),
      firstDate.getDate() + offset,
    );
    for (const event of getNotificationEvents(
      snapshot,
      date,
      settings.endWarningMinutes,
    )) {
      if (event.triggerTime.getTime() <= now.getTime()) continue;
      if (!matchesPeriod(audience, new Set(event.occurrence.period.classIds ?? []))) continue;
      if (event.kind === 'start' && !settings.lessonStartEnabled) continue;
      if (event.kind === 'end-warning' && !settings.endWarningEnabled) continue;

      desired.push({
        identifier: event.key,
        triggerTime: event.triggerTime,
        kind: event.kind,
        title: event.kind === 'start' ? 'Lesson starting' : 'Lesson ending soon',
        body:
          event.kind === 'start'
            ? event.occurrence.period.name
            : `${event.occurrence.period.name} ends in ${settings.endWarningMinutes} minutes`,
      });
    }
  }

  return desired
    .sort(
      (left, right) =>
        left.triggerTime.getTime() - right.triggerTime.getTime() ||
        (left.identifier < right.identifier ? -1 : left.identifier > right.identifier ? 1 : 0),
    )
    .slice(0, NOTIFICATION_LIMIT);
}

export function diffScheduledNotifications(
  desired: readonly DesiredNotification[],
  actual: readonly ActualNotification[],
): ReconcileDiff {
  const desiredById = new Map(desired.map((item) => [item.identifier, item]));
  const actualById = new Map(actual.map((item) => [item.identifier, item]));
  const cancel: string[] = [];
  const schedule: DesiredNotification[] = [];

  for (const item of actual) {
    const wanted = desiredById.get(item.identifier);
    if (!wanted || item.triggerTimeMs !== wanted.triggerTime.getTime()) {
      cancel.push(item.identifier);
    }
  }
  for (const item of desired) {
    const existing = actualById.get(item.identifier);
    if (!existing || existing.triggerTimeMs !== item.triggerTime.getTime()) {
      schedule.push(item);
    }
  }
  return { cancel, schedule };
}

export async function reconcileScheduledNotifications(
  snapshot: ScheduleSnapshot,
  audience: DeviceAudience,
  settings: NotificationSettings,
  now = new Date(),
): Promise<void> {
  const desired = buildDesiredNotifications(snapshot, audience, settings, now);
  const scheduled = await Notifications.getAllScheduledNotificationsAsync();
  const ours = scheduled
    .filter((request) => isLessonIdentifier(request.identifier))
    .map((request) => ({
      identifier: request.identifier,
      triggerTimeMs:
        typeof request.content.data?.aqiTriggerTime === 'number'
          ? request.content.data.aqiTriggerTime
          : null,
    }));
  const diff = diffScheduledNotifications(desired, ours);

  await Promise.all(
    diff.cancel.map((identifier) =>
      Notifications.cancelScheduledNotificationAsync(identifier),
    ),
  );
  for (const item of diff.schedule) {
    await Notifications.scheduleNotificationAsync({
      identifier: item.identifier,
      content: {
        title: item.title,
        body: item.body,
        sound: true,
        data: {
          aqiClock: true,
          aqiTriggerTime: item.triggerTime.getTime(),
          kind: item.kind,
        },
      },
      trigger: {
        type: Notifications.SchedulableTriggerInputTypes.DATE,
        date: item.triggerTime,
        channelId: NOTIFICATION_CHANNEL_ID,
      },
    });
  }
}

export async function reconcileScheduledNotificationsFromCache(): Promise<void> {
  const supabase = getSupabaseClient();
  const { data } = await supabase.auth.getSession();
  if (!data.session) return;

  const snapshot = await loadScheduleSnapshot();
  let audience: DeviceAudience;
  if (data.session.user.is_anonymous) {
    const preferences = await getStudentPreferences();
    if (!preferences?.selectedClassIds.length) {
      await cancelScheduledLessonNotifications();
      return;
    }
    audience = {
      role: 'StudentDevice',
      selectedClassIds: new Set(preferences.selectedClassIds),
      optedHalfDays: new Set([
        ...(preferences.optedAm ? (['am'] as const) : []),
        ...(preferences.optedPm ? (['pm'] as const) : []),
      ]),
    };
  } else {
    const profile = await getCachedProfile(data.session.user.id);
    audience = {
      role: profile?.role === 'admin' ? 'Admin' : 'Teacher',
      selectedClassIds: new Set(),
      optedHalfDays: new Set(),
    };
  }
  await reconcileScheduledNotifications(
    snapshot,
    audience,
    await getNotificationSettings(),
  );
}

export async function cancelScheduledLessonNotifications(): Promise<void> {
  const scheduled = await Notifications.getAllScheduledNotificationsAsync();
  await Promise.all(
    scheduled
      .filter((request) => isLessonIdentifier(request.identifier))
      .map((request) =>
        Notifications.cancelScheduledNotificationAsync(request.identifier),
      ),
  );
}

function isLessonIdentifier(identifier: string): boolean {
  return identifier.startsWith('start:') || identifier.startsWith('end-warning:');
}
