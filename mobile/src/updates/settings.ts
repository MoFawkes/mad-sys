import { getMeta, setMeta } from '@/src/data/sqlite';

const KEY = 'include_beta_updates';
const listeners = new Set<() => void>();

export async function getIncludeBetaUpdates(): Promise<boolean> {
  return (await getMeta(KEY)) === 'true';
}

export async function saveIncludeBetaUpdates(value: boolean): Promise<void> {
  await setMeta(KEY, String(value));
  // A changed channel should be checked on the next foreground/start.
  await setMeta('updates_last_check', '0');
  for (const listener of listeners) listener();
}

export function subscribeUpdateSettings(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
