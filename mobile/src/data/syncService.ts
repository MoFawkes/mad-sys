import { RealtimeChannel } from '@supabase/supabase-js';

import { getSupabaseClient } from './sessionStore';
import { getLastSyncedAt } from './repositories';
import {
  getMeta,
  RemoteRow,
  replaceSnapshot,
  setMeta,
  SYNC_TABLES,
  SyncTable,
  wipeCache,
} from './sqlite';
import { readStoreLastSyncedAt } from './syncState';

export type ConnectivityState = 'offline' | 'syncing' | 'online';
export type SyncState = {
  connectivity: ConnectivityState;
  lastSyncedAt: Date | null;
  error: string | null;
};

type Listener = (state: SyncState, changedTable?: SyncTable) => void;
export type SyncAudience = 'teacher' | 'student';
export const STUDENT_DEVICE_REVOKED_MESSAGE =
  'This device is no longer enrolled. Ask for a new join code.';

const PROFILE_FIRST: readonly SyncTable[] = [
  'profiles',
  ...SYNC_TABLES.filter((table) => table !== 'profiles'),
];

// Students have no readable profiles row under RLS, so pulling the table only
// ever yields an empty snapshot. Omitting it matches the desktop client's
// TablesForAudience and avoids a per-sync query that can never return data.
const STUDENT_TABLES: readonly SyncTable[] = SYNC_TABLES.filter(
  (table) => table !== 'profiles',
);

export function syncOrderFor(audience: SyncAudience): readonly SyncTable[] {
  return audience === 'teacher' ? PROFILE_FIRST : STUDENT_TABLES;
}

export class SyncService {
  private state: SyncState = { connectivity: 'offline', lastSyncedAt: null, error: null };
  private readonly listeners = new Set<Listener>();
  private readonly channels = new Map<SyncTable, RealtimeChannel>();
  private readonly debounces = new Map<SyncTable, ReturnType<typeof setTimeout>>();
  private active = false;
  private syncInFlight: Promise<void> | null = null;
  private userId: string | null = null;
  private audience: SyncAudience = 'teacher';

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    listener(this.state);
    return () => this.listeners.delete(listener);
  }

  async start(userId: string, audience: SyncAudience = 'teacher'): Promise<void> {
    if (this.active && this.userId === userId && this.audience === audience) return;
    await this.stop();
    this.active = true;
    this.userId = userId;
    this.audience = audience;
    const cachedLastSync = await getLastSyncedAt();
    if (cachedLastSync) this.update({ lastSyncedAt: cachedLastSync });
    this.subscribeRealtime();
    await this.syncAll();
  }

  async stop(): Promise<void> {
    this.active = false;
    this.userId = null;
    this.audience = 'teacher';
    for (const timer of this.debounces.values()) clearTimeout(timer);
    this.debounces.clear();
    const supabase = getSupabaseClient();
    await Promise.all([...this.channels.values()].map((channel) => supabase.removeChannel(channel)));
    this.channels.clear();
    this.update({ connectivity: 'offline', error: null });
  }

  async syncAll(): Promise<void> {
    if (this.syncInFlight) return this.syncInFlight;
    this.syncInFlight = this.runSyncAll().finally(() => {
      this.syncInFlight = null;
    });
    return this.syncInFlight;
  }

  async syncTable(table: SyncTable): Promise<void> {
    if (!this.active) return;
    this.update({ connectivity: 'syncing', error: null });
    try {
      await this.pullAndReplace(table);
      const lastSyncedAt = await readStoreLastSyncedAt(getLastSyncedAt);
      this.update({ connectivity: 'online', lastSyncedAt, error: null }, table);
    } catch (error) {
      this.update({
        connectivity: 'offline',
        error: error instanceof Error ? error.message : 'Sync failed.',
      });
    }
  }

  signalTableChanged(table: SyncTable): void {
    const existing = this.debounces.get(table);
    if (existing) clearTimeout(existing);
    this.debounces.set(
      table,
      setTimeout(() => {
        this.debounces.delete(table);
        void this.syncTable(table);
      }, 500),
    );
  }

  private async runSyncAll(): Promise<void> {
    if (!this.active) return;
    this.update({ connectivity: 'syncing', error: null });
    try {
      if (this.audience === 'student' && (await this.studentDeviceWasRevoked())) {
        await wipeCache();
        this.update({
          connectivity: 'online',
          lastSyncedAt: null,
          error: STUDENT_DEVICE_REVOKED_MESSAGE,
        });
        return;
      }
      for (const table of syncOrderFor(this.audience)) {
        await this.pullAndReplace(table);
        this.update({}, table);
      }
      const lastSyncedAt = await readStoreLastSyncedAt(getLastSyncedAt);
      this.update({ connectivity: 'online', lastSyncedAt, error: null });
    } catch (error) {
      this.update({
        connectivity: 'offline',
        error: error instanceof Error ? error.message : 'Sync failed.',
      });
    }
  }

  private async studentDeviceWasRevoked(): Promise<boolean> {
    if ((await getMeta('student_enrolled')) !== 'true') return false;
    const supabase = getSupabaseClient();
    const { data, error } = await supabase
      .from('student_devices')
      .select('user_id')
      .limit(1);
    if (error) throw error;
    return (data?.length ?? 0) === 0;
  }

  private async pullAndReplace(table: SyncTable): Promise<void> {
    const supabase = getSupabaseClient();
    const { data, error } = await supabase.from(table).select('*');
    if (error) throw error;
    const rows = (data ?? []) as RemoteRow[];

    if (table === 'profiles' && this.userId && this.audience === 'teacher') {
      const ownProfile = rows.find((row) => row.id === this.userId);
      const organizationId =
        typeof ownProfile?.org_id === 'string' ? ownProfile.org_id : null;
      const cachedOrganizationId = await getMeta('org_id');
      if (
        organizationId &&
        cachedOrganizationId &&
        organizationId.toLowerCase() !== cachedOrganizationId.toLowerCase()
      ) {
        await wipeCache();
      }
      if (organizationId) await setMeta('org_id', organizationId);
    }

    // Realtime payloads never reach this method; every signal performs a full table snapshot.
    await replaceSnapshot(table, rows);
  }

  private subscribeRealtime(): void {
    const supabase = getSupabaseClient();
    for (const table of syncOrderFor(this.audience)) {
      const channel = supabase
        .channel(`mobile-${table}`)
        .on(
          'postgres_changes',
          { event: '*', schema: 'public', table },
          () => this.signalTableChanged(table),
        )
        .subscribe();
      this.channels.set(table, channel);
    }
  }

  private update(patch: Partial<SyncState>, changedTable?: SyncTable): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener(this.state, changedTable);
  }
}

export const syncService = new SyncService();
