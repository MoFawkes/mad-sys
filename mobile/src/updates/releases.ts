const RELEASES_API = 'https://api.github.com/repos/MoFawkes/aqi-clock-releases/releases?per_page=100';
const DOWNLOAD_ROOT = 'https://github.com/MoFawkes/aqi-clock-releases/releases/download/';

type Version = { core: number[]; pre: string[] };
export type Release = {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets: { name: string; browser_download_url: string }[];
};
export type AndroidUpdate = { version: string; url: string };

function isRelease(value: unknown): value is Release {
  if (!value || typeof value !== 'object') return false;
  const release = value as Partial<Release>;
  return typeof release.tag_name === 'string'
    && typeof release.draft === 'boolean'
    && typeof release.prerelease === 'boolean'
    && Array.isArray(release.assets)
    && release.assets.every(asset => asset && typeof asset.name === 'string'
      && typeof asset.browser_download_url === 'string');
}

function parseVersion(value: string): Version | null {
  const match = /^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([\da-zA-Z-]+(?:\.[\da-zA-Z-]+)*))?(?:\+[\da-zA-Z.-]+)?$/.exec(value);
  if (!match) return null;
  const core = match.slice(1, 4).map(Number);
  const pre = match[4]?.split('.') ?? [];
  if (!core.every(Number.isSafeInteger) || pre.some(part => /^0\d+$/.test(part))) return null;
  return { core, pre };
}

function compare(a: Version, b: Version): number {
  for (let i = 0; i < 3; i++) if (a.core[i] !== b.core[i]) return a.core[i] - b.core[i];
  if (!a.pre.length || !b.pre.length) return a.pre.length === b.pre.length ? 0 : a.pre.length ? -1 : 1;
  for (let i = 0; i < Math.max(a.pre.length, b.pre.length); i++) {
    const left = a.pre[i], right = b.pre[i];
    if (left === undefined) return -1;
    if (right === undefined) return 1;
    if (left === right) continue;
    const ln = /^\d+$/.test(left), rn = /^\d+$/.test(right);
    if (ln && rn) return left.length - right.length || (left < right ? -1 : 1);
    if (ln !== rn) return ln ? -1 : 1;
    return left < right ? -1 : 1;
  }
  return 0;
}

export function selectAndroidUpdate(releases: Release[], current: string, includeBeta: boolean): AndroidUpdate | null {
  const installed = parseVersion(current);
  if (!installed) return null;
  let best = installed;
  let update: AndroidUpdate | null = null;
  for (const release of releases) {
    const version = parseVersion(release.tag_name);
    if (release.draft || !version || (!includeBeta && (release.prerelease || version.pre.length > 0)) || compare(version, best) <= 0) continue;
    const name = `AqiClock-${release.tag_name.replace(/^v/, '')}-android.apk`;
    const expectedUrl = `${DOWNLOAD_ROOT}${release.tag_name}/${name}`;
    const apk = release.assets.find(asset => asset.name === name && asset.browser_download_url === expectedUrl);
    if (!apk) continue;
    best = version;
    update = { version: release.tag_name.replace(/^v/, ''), url: apk.browser_download_url };
  }
  return update;
}

export async function fetchAndroidUpdate(current: string, includeBeta: boolean): Promise<AndroidUpdate | null> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15000);
  try {
    const response = await fetch(RELEASES_API, { signal: controller.signal, headers: { Accept: 'application/vnd.github+json' } });
    if (!response.ok) throw new Error('Could not check for updates. Try again later.');
    const releases: unknown = await response.json();
    if (!Array.isArray(releases)) throw new Error('Invalid update response.');
    return selectAndroidUpdate(releases.filter(isRelease), current, includeBeta);
  } finally {
    clearTimeout(timeout);
  }
}
