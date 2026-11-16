import { PULL_PAGE_SIZE, pullAllRows } from '@/src/data/pullTable';

jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));

type Call = { table: string; order: string[]; from: number; to: number };

// Mimics PostgREST: an ordered range read capped at `serverCap` rows per response.
function fakeSupabase(total: number, serverCap: number, reportCount = true) {
  const calls: Call[] = [];
  const client = {
    from(table: string) {
      const order: string[] = [];
      const query = {
        select: () => query,
        order(column: string) {
          order.push(column);
          return query;
        },
        async range(from: number, to: number) {
          calls.push({ table, order, from, to });
          const end = Math.min(total, to + 1, from + serverCap);
          const data = Array.from({ length: Math.max(0, end - from) }, (_, index) => ({
            id: `row-${from + index}`,
          }));
          return { data, error: null, count: reportCount ? total : null };
        },
      };
      return query;
    },
  };
  return { client: client as never, calls };
}

describe('pullAllRows', () => {
  it('reads a small table in one ordered request', async () => {
    const { client, calls } = fakeSupabase(3, 1000);
    const rows = await pullAllRows(client, 'announcements');
    expect(rows).toHaveLength(3);
    expect(calls).toEqual([
      { table: 'announcements', order: ['id'], from: 0, to: PULL_PAGE_SIZE - 1 },
    ]);
  });

  it('keeps reading past the server row cap until every row is fetched', async () => {
    const { client, calls } = fakeSupabase(2345, 1000);
    const rows = await pullAllRows(client, 'announcements');
    expect(rows).toHaveLength(2345);
    expect(new Set(rows.map((row) => row.id)).size).toBe(2345);
    expect(calls.map((call) => call.from)).toEqual([0, 1000, 2000]);
  });

  it('still fetches everything when the server cap is below the page size', async () => {
    const { client } = fakeSupabase(1200, 500);
    expect(await pullAllRows(client, 'date_overrides')).toHaveLength(1200);
  });

  it('falls back to reading until an empty page when no count is reported', async () => {
    const { client, calls } = fakeSupabase(1500, 1000, false);
    expect(await pullAllRows(client, 'periods')).toHaveLength(1500);
    expect(calls).toHaveLength(3);
  });

  it('orders the link table by its composite key', async () => {
    const { client, calls } = fakeSupabase(1, 1000);
    await pullAllRows(client, 'period_classes');
    expect(calls[0].order).toEqual(['period_id', 'class_id']);
  });

  it('surfaces a failed page instead of returning a partial table', async () => {
    const failure = new Error('network down');
    const query = {
      select: () => query,
      order: () => query,
      range: async () => ({ data: null, error: failure, count: null }),
    };
    await expect(
      pullAllRows({ from: () => query } as never, 'timetables'),
    ).rejects.toBe(failure);
  });
});
