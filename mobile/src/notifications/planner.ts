import * as Notifications from 'expo-notifications';

import {
  getCachedProfile,
  getOrganization,
  getStudentPreferences,
  loadScheduleSnapshot,
  recordNotificationScheduleSnapshot,
} from '@/src/data/repositories';
import { getSupabaseClient } from '@/src/data/sessionStore';
import {
  DeviceAudience,
  filterScheduleForAudience,
  getNotificationEvents,
  matchesPeriod,
  ScheduleSnapshot,
} from '@/src/domain';

import { getNotificationSettings, NotificationSettings } from './settings';
import { toInstituteWallClock, wallClockToInstant } from '@/src/time/instituteTime';

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
  title: string | null;
  body: string | null;
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
  instituteTimeZone?: string,
): DesiredNotification[] {
  const desired: DesiredNotification[] = [];
  const instituteNow = toInstituteWallClock(now, instituteTimeZone);
  const firstDate = new Date(instituteNow.getFullYear(), instituteNow.getMonth(), instituteNow.getDate());

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
      const triggerTime = wallClockToInstant(event.triggerTime, instituteTimeZone);
      if (triggerTime.getTime() <= now.getTime()) continue;
      if (!matchesPeriod(audience, new Set(event.occurrence.period.classIds ?? []))) continue;
      if (event.kind === 'start' && !settings.lessonStartEnabled) continue;
      if (event.kind === 'end-warning' && !settings.endWarningEnabled) continue;

      desired.push({
        identifier: event.key,
        triggerTime,
        kind: event.kind,
        title: event.kind === 'start' ? 'Lesson starting' : 'Lesson ending soon',
        body:
          event.kind === 'start'
            ? event.occurrence.period.name
            : `${event.occurrence.period.name} ends in ${settings.endWarningMinutes} minutes`,
      });
    }
  }

  const grouped = new Map<string, DesiredNotification[]>();
  for (const item of desired) {
    const key = `${item.triggerTime.getTime()}:${item.kind}`;
    grouped.set(key, [...(grouped.get(key) ?? []), item]);
  }

  return [...grouped.values()]
    .map((items) => {
      const sorted = [...items].sort((left, right) =>
        left.identifier < right.identifier ? -1 : left.identifier > right.identifier ? 1 : 0,
      );
      const first = sorted[0];
      return {
        ...first,
        body: sorted.map((item) => item.body).join('\n'),
      };
    })
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
    if (
      !wanted ||
      item.triggerTimeMs !== wanted.triggerTime.getTime() ||
      item.title !== wanted.title ||
      item.body !== wanted.body
    ) {
      cancel.push(item.identifier);
    }
  }
  for (const item of desired) {
    const existing = actualById.get(item.identifier);
    if (
      !existing ||
      existing.triggerTimeMs !== item.triggerTime.getTime() ||
      existing.title !== item.title ||
      existing.body !== item.body
    ) {
      schedule.push(item);
    }
  }
  return { cancel, schedule };
}

let reconciliationChain: Promise<void> = Promise.resolve();

export function reconcileScheduledNotifications(
  snapshot: ScheduleSnapshot,
  audience: DeviceAudience,
  settings: NotificationSettings,
  now = new Date(),
  instituteTimeZone?: string,
): Promise<void> {
  const operation = reconciliationChain.then(() => reconcileScheduledNotificationsCore(
    snapshot, audience, settings, now, instituteTimeZone,
  ));
  reconciliationChain = operation.catch(() => undefined);
  return operation;
}

async function reconcileScheduledNotificationsCore(
  snapshot: ScheduleSnapshot,
  audience: DeviceAudience,
  settings: NotificationSettings,
  now: Date,
  instituteTimeZone?: string,
): Promise<void> {
  const desired = buildDesiredNotifications(snapshot, audience, settings, now, instituteTimeZone);
  const scheduled = await Notifications.getAllScheduledNotificationsAsync();
  const ours = scheduled
    .filter((request) => isLessonIdentifier(request.identifier))
    .map((request) => ({
      identifier: request.identifier,
      triggerTimeMs:
        typeof request.content.data?.aqiTriggerTime === 'number'
          ? request.content.data.aqiTriggerTime
          : null,
      title: request.content.title ?? null,
      body: request.content.body ?? null,
    }));
  const diff = diffScheduledNotifications(desired, ours);

  await recordNotificationScheduleSnapshot(JSON.stringify({
    desired: desired.map((item) => ({
      identifier: item.identifier,
      triggerTime: item.triggerTime.toISOString(),
      title: item.title,
      body: item.body,
      kind: item.kind,
    })),
    actual: ours.map((item) => ({
      ...item,
      triggerTime: item.triggerTimeMs == null
        ? null
        : new Date(item.triggerTimeMs).toISOString(),
    })),
  }));

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
  const organization = await getOrganization();
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
    filterScheduleForAudience(snapshot, audience),
    audience,
    await getNotificationSettings(),
    new Date(),
    organization?.timeZone,
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

export async function cancelAllScheduledAqiClockNotifications(): Promise<void> {
  await Notifications.cancelAllScheduledNotificationsAsync();
}

function isLessonIdentifier(identifier: string): boolean {
  return identifier.startsWith('start:') || identifier.startsWith('end-warning:');
}
