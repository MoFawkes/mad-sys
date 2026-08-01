export async function readStoreLastSyncedAt(
  read: () => Promise<Date | null>,
): Promise<Date | null> {
  return read();
}
