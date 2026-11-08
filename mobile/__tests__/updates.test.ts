import { fetchAndroidUpdate, Release, selectAndroidUpdate } from '@/src/updates/releases';
import { getIncludeBetaUpdates, saveIncludeBetaUpdates } from '@/src/updates/settings';
import { getMeta, setMeta } from '@/src/data/sqlite';

jest.mock('@/src/data/sqlite', () => ({ getMeta: jest.fn(), setMeta: jest.fn() }));

function release(version: string, prerelease = version.includes('-')): Release {
  const name = `AqiClock-${version}-android.apk`;
  return { tag_name: `v${version}`, draft: false, prerelease, assets: [{ name, browser_download_url: `https://github.com/MoFawkes/aqi-clock-releases/releases/download/v${version}/${name}` }] };
}

describe('Android release selection', () => {
  const releases = [release('0.16.0-beta.10'), release('0.16.0-beta.2'), release('0.15.4')];
  it('keeps stable users on stable unless explicitly opted in', () => {
    expect(selectAndroidUpdate(releases, '0.15.4', false)).toBeNull();
    expect(selectAndroidUpdate(releases, '0.15.4', true)?.version).toBe('0.16.0-beta.10');
    expect(selectAndroidUpdate([release('0.17.0-beta.1', false)], '0.15.4', false)).toBeNull();
  });
  it('never downgrades when beta is switched off, and moves to a newer stable', () => {
    expect(selectAndroidUpdate(releases, '0.16.0-beta.10', false)).toBeNull();
    expect(selectAndroidUpdate([...releases, release('0.16.0')], '0.16.0-beta.10', false)?.version).toBe('0.16.0');
    expect(selectAndroidUpdate([release('0.16.0-beta.20')], '0.16.0', true)).toBeNull();
  });
  it('ignores drafts, missing APKs, invalid versions and untrusted download URLs', () => {
    expect(selectAndroidUpdate([
      { ...release('0.17.0'), draft: true },
      { ...release('0.18.0'), assets: [] },
      release('01.19.0'),
      { ...release('0.20.0'), assets: [{ name: 'AqiClock-0.20.0-android.apk', browser_download_url: 'https://evil.example/app.apk' }] },
    ], '0.15.4', true)).toBeNull();
  });
  it('orders numeric core and prerelease components correctly', () => {
    expect(selectAndroidUpdate([release('0.9.0'), release('0.10.0')], '0.8.0', true)?.version).toBe('0.10.0');
    expect(selectAndroidUpdate([release('1.0.0-beta'), release('1.0.0-beta.1'), release('1.0.0-rc.1')], '0.9.0', true)?.version).toBe('1.0.0-rc.1');
  });
});

describe('update preferences and network errors', () => {
  beforeEach(() => jest.clearAllMocks());
  it('defaults off and persists an explicit opt-in', async () => {
    jest.mocked(getMeta).mockResolvedValue(null);
    expect(await getIncludeBetaUpdates()).toBe(false);
    await saveIncludeBetaUpdates(true);
    expect(setMeta).toHaveBeenCalledWith('include_beta_updates', 'true');
    expect(setMeta).toHaveBeenCalledWith('updates_last_check', '0');
  });
  it('reports network/rate-limit errors instead of saying up to date', async () => {
    const originalFetch = global.fetch;
    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 403 });
    try { await expect(fetchAndroidUpdate('0.15.4', true)).rejects.toThrow('Could not check'); }
    finally { global.fetch = originalFetch; }
  });
});
