import type { SupabaseClient } from '@supabase/supabase-js';

import type { RemoteRow, SyncTable } from './sqlite';

// PostgREST silently caps every response at the project's max_rows setting, and
// the cache replaces a table with whatever a pull returns. Reading in ordered
// pages until the server's own exact count is reached keeps rows beyond the cap
// from disappearing from the device.
export const PULL_PAGE_SIZE = 1000;

const ORDER_COLUMNS: Partial<Record<SyncTable, readonly string[]>> = {
  period_classes: ['period_id', 'class_id'],
};

export async function pullAllRows(
  supabase: Pick<SupabaseClient, 'from'>,
  table: SyncTable,
): Promise<RemoteRow[]> {
  const rows: RemoteRow[] = [];
  for (;;) {
    let query = supabase.from(table).select('*', { count: 'exact' });
    for (const column of ORDER_COLUMNS[table] ?? ['id']) {
      query = query.order(column, { ascending: true });
    }
    const { data, error, count } = await query.range(
      rows.length,
      rows.length + PULL_PAGE_SIZE - 1,
    );
    if (error) throw error;
    const page = (data ?? []) as RemoteRow[];
    rows.push(...page);
    // Without a usable count, keep reading until a page comes back empty.
    if (page.length === 0 || (count != null && rows.length >= count)) return rows;
  }
}
