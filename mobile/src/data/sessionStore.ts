import { createClient, processLock, SupabaseClient } from '@supabase/supabase-js';
import Constants from 'expo-constants';
import * as SecureStore from 'expo-secure-store';

const CHUNK_SIZE = 1800;

type StorageBackend = {
  getItemAsync(key: string): Promise<string | null>;
  setItemAsync(key: string, value: string): Promise<void>;
  deleteItemAsync(key: string): Promise<void>;
};

type SupabaseStorage = {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
};

type Manifest = { chunks: number; generation: number };

export function createChunkedStorage(backend: StorageBackend): SupabaseStorage {
  const manifestKey = (key: string) => `${key}.manifest`;
  const chunkKey = (key: string, generation: number, index: number) =>
    generation === 0 ? `${key}.${index}` : `${key}.g${generation}.${index}`;

  async function readManifest(key: string): Promise<Manifest | null> {
    const value = await backend.getItemAsync(manifestKey(key));
    if (!value) return null;
    try {
      const parsed = JSON.parse(value) as Manifest;
      return Number.isInteger(parsed.chunks) && parsed.chunks >= 0
        ? {
            chunks: parsed.chunks,
            generation:
              Number.isInteger(parsed.generation) && parsed.generation >= 0
                ? parsed.generation
                : 0,
          }
        : null;
    } catch {
      return null;
    }
  }

  return {
    async getItem(key) {
      const manifest = await readManifest(key);
      if (!manifest) return null;
      const chunks = await Promise.all(
        Array.from({ length: manifest.chunks }, (_, index) =>
          backend.getItemAsync(chunkKey(key, manifest.generation, index)),
        ),
      );
      return chunks.some((chunk) => chunk == null) ? null : chunks.join('');
    },

    async setItem(key, value) {
      const previous = await readManifest(key);
      const generation = (previous?.generation ?? 0) + 1;
      const chunks = Array.from(
        { length: Math.ceil(value.length / CHUNK_SIZE) },
        (_, index) => value.slice(index * CHUNK_SIZE, (index + 1) * CHUNK_SIZE),
      );
      await Promise.all(
        chunks.map((chunk, index) =>
          backend.setItemAsync(chunkKey(key, generation, index), chunk),
        ),
      );
      // A generation flip makes an interrupted write resolve entirely to old or new chunks.
      await backend.setItemAsync(
        manifestKey(key),
        JSON.stringify({ chunks: chunks.length, generation }),
      );
      if (previous) {
        await Promise.all(
          Array.from({ length: previous.chunks }, (_, index) =>
            backend.deleteItemAsync(chunkKey(key, previous.generation, index)),
          ),
        );
      }
    },

    async removeItem(key) {
      const manifest = await readManifest(key);
      await backend.deleteItemAsync(manifestKey(key));
      if (manifest) {
        await Promise.all(
          Array.from({ length: manifest.chunks }, (_, index) =>
            backend.deleteItemAsync(chunkKey(key, manifest.generation, index)),
          ),
        );
      }
    },
  };
}

export const secureSessionStorage = createChunkedStorage({
  getItemAsync: SecureStore.getItemAsync,
  setItemAsync: SecureStore.setItemAsync,
  deleteItemAsync: SecureStore.deleteItemAsync,
});

let client: SupabaseClient | null = null;

export function getSupabaseClient(): SupabaseClient {
  if (client) return client;

  const extra = Constants.expoConfig?.extra as
    | { supabaseUrl?: string; supabaseAnonKey?: string }
    | undefined;
  const url = extra?.supabaseUrl ?? process.env.EXPO_PUBLIC_SUPABASE_URL;
  const publishableKey =
    extra?.supabaseAnonKey ?? process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY;
  if (!url || !publishableKey) {
    throw new Error('Supabase mobile configuration is missing.');
  }

  client = createClient(url, publishableKey, {
    auth: {
      storage: secureSessionStorage,
      autoRefreshToken: true,
      persistSession: true,
      detectSessionInUrl: false,
      lock: processLock,
    },
  });
  return client;
}
