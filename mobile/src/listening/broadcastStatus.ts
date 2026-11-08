export const PLAYER_URL = 'https://emasjidlive.co.uk/miniplayer/alqalaminstitute';

export function parseAvailability(html: string): boolean | null {
  if (/<small\b[^>]*\bclass\s*=\s*["'][^"']*\bstatus-label\b[^"']*["'][^>]*>\s*Currently\s+Offline\s*<\/small>/i.test(html)) return false;
  if (/<audio\b[^>]*\bid\s*=\s*["']track["']/i.test(html)) return true;
  return null;
}

export async function fetchAvailability(signal: AbortSignal): Promise<boolean | null> {
  try {
    const response = await fetch(PLAYER_URL, { signal, cache: 'no-store' });
    return response.ok ? parseAvailability(await response.text()) : null;
  } catch {
    return null;
  }
}
