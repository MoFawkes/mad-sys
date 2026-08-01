import { createChunkedStorage } from '@/src/data/sessionStore';

function memoryBackend() {
  const values = new Map<string, string>();
  return {
    values,
    backend: {
      getItemAsync: async (key: string) => values.get(key) ?? null,
      setItemAsync: async (key: string, value: string) => {
        values.set(key, value);
      },
      deleteItemAsync: async (key: string) => {
        values.delete(key);
      },
    },
  };
}

describe('chunked SecureStore adapter', () => {
  it('round-trips a session larger than the SecureStore value cap', async () => {
    const memory = memoryBackend();
    const storage = createChunkedStorage(memory.backend);
    const session = JSON.stringify({ access_token: 'a'.repeat(5000), refresh_token: 'b'.repeat(5000) });
    await storage.setItem('session', session);
    expect(await storage.getItem('session')).toBe(session);
    expect([...memory.values.keys()].filter((key) => key.startsWith('session.')).length).toBeGreaterThan(2);
  });

  it('removes stale chunks when a value shrinks and clears every chunk', async () => {
    const memory = memoryBackend();
    const storage = createChunkedStorage(memory.backend);
    await storage.setItem('session', 'x'.repeat(6000));
    await storage.setItem('session', 'short');
    expect([...memory.values.keys()].sort()).toEqual(['session.g2.0', 'session.manifest']);
    await storage.removeItem('session');
    expect(memory.values.size).toBe(0);
  });

  it('returns null rather than a partial value when a chunk is missing', async () => {
    const memory = memoryBackend();
    const storage = createChunkedStorage(memory.backend);
    await storage.setItem('session', 'x'.repeat(4000));
    memory.values.delete('session.g1.1');
    expect(await storage.getItem('session')).toBeNull();
  });

  it('keeps the previous generation readable when a replacement is interrupted', async () => {
    const memory = memoryBackend();
    const storage = createChunkedStorage(memory.backend);
    const oldValue = 'old'.repeat(1000);
    await storage.setItem('session', oldValue);
    const originalSet = memory.backend.setItemAsync;
    memory.backend.setItemAsync = async (key, value) => {
      if (key === 'session.g2.1') throw new Error('simulated process interruption');
      await originalSet(key, value);
    };
    await expect(storage.setItem('session', 'new'.repeat(2000))).rejects.toThrow();
    expect(await storage.getItem('session')).toBe(oldValue);
  });
});
