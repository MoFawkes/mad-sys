import { getMeta, setMeta } from '@/src/data/sqlite';

export type NotificationSettings = {
  lessonStartEnabled: boolean;
  endWarningEnabled: boolean;
  endWarningMinutes: number;
  announcementsEnabled: boolean;
};

export const DEFAULT_NOTIFICATION_SETTINGS: NotificationSettings = {
  lessonStartEnabled: true,
  endWarningEnabled: true,
  endWarningMinutes: 5,
  announcementsEnabled: true,
};

const SETTINGS_KEY = 'notification_settings';
const listeners = new Set<() => void>();

export async function getNotificationSettings(): Promise<NotificationSettings> {
  const stored = await getMeta(SETTINGS_KEY);
  if (!stored) return DEFAULT_NOTIFICATION_SETTINGS;
  try {
    const parsed = JSON.parse(stored) as Partial<NotificationSettings>;
    return normalizeNotificationSettings(parsed);
  } catch {
    return DEFAULT_NOTIFICATION_SETTINGS;
  }
}

export async function saveNotificationSettings(
  settings: NotificationSettings,
): Promise<NotificationSettings> {
  const normalized = normalizeNotificationSettings(settings);
  await setMeta(SETTINGS_KEY, JSON.stringify(normalized));
  for (const listener of listeners) listener();
  return normalized;
}

export function subscribeNotificationSettings(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function normalizeNotificationSettings(
  settings: Partial<NotificationSettings>,
): NotificationSettings {
  const lead = Number.isFinite(settings.endWarningMinutes)
    ? Math.round(settings.endWarningMinutes!)
    : DEFAULT_NOTIFICATION_SETTINGS.endWarningMinutes;
  return {
    lessonStartEnabled:
      settings.lessonStartEnabled ?? DEFAULT_NOTIFICATION_SETTINGS.lessonStartEnabled,
    endWarningEnabled:
      settings.endWarningEnabled ?? DEFAULT_NOTIFICATION_SETTINGS.endWarningEnabled,
    endWarningMinutes: Math.min(15, Math.max(0, lead)),
    announcementsEnabled:
      settings.announcementsEnabled ?? DEFAULT_NOTIFICATION_SETTINGS.announcementsEnabled,
  };
}
